using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;

namespace DMS.Desktop.Services.Mes;

/// <summary>
/// Minimal read-only Modbus TCP client used by MESDPM.
/// It intentionally implements only functions 01, 02, 03 and 04.
/// There are no write functions in this class.
/// </summary>
public sealed class ReadOnlyModbusTcpClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly int _timeoutMs;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ushort _transactionId;
    private bool _disposed;

    public ReadOnlyModbusTcpClient(
        string host,
        int port,
        byte unitId,
        int timeoutMs)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
        _timeoutMs = Math.Max(250, timeoutMs);
    }

    public Task<bool[]> ReadCoilsAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken) =>
        ReadBitsAsync(0x01, startAddress, count, cancellationToken);

    public Task<bool[]> ReadDiscreteInputsAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken) =>
        ReadBitsAsync(0x02, startAddress, count, cancellationToken);

    public Task<ushort[]> ReadHoldingRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken) =>
        ReadRegistersAsync(0x03, startAddress, count, cancellationToken);

    public Task<ushort[]> ReadInputRegistersAsync(
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken) =>
        ReadRegistersAsync(0x04, startAddress, count, cancellationToken);

    private async Task<bool[]> ReadBitsAsync(
        byte functionCode,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken)
    {
        if (count is < 1 or > 2000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Modbus bit count must be between 1 and 2000.");
        }

        var response = await ExecuteReadAsync(
            functionCode,
            startAddress,
            count,
            cancellationToken).ConfigureAwait(false);

        if (response.Length < 2)
        {
            throw new IOException("The Modbus bit response is too short.");
        }

        var byteCount = response[1];

        var expectedMinimumByteCount = (count + 7) / 8;

        if (byteCount < expectedMinimumByteCount ||
            response.Length != byteCount + 2)
        {
            throw new IOException(
                $"The Modbus bit response length is invalid. " +
                $"ExpectedAtLeast={expectedMinimumByteCount}; ByteCount={byteCount}; Actual={response.Length - 2}.");
        }

        var values = new bool[count];

        for (var index = 0; index < count; index++)
        {
            var byteIndex = index / 8;
            var bitIndex = index % 8;
            values[index] = (response[2 + byteIndex] & (1 << bitIndex)) != 0;
        }

        return values;
    }

    private async Task<ushort[]> ReadRegistersAsync(
        byte functionCode,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken)
    {
        if (count is < 1 or > 125)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Modbus register count must be between 1 and 125.");
        }

        var response = await ExecuteReadAsync(
            functionCode,
            startAddress,
            count,
            cancellationToken).ConfigureAwait(false);

        if (response.Length < 2)
        {
            throw new IOException("The Modbus register response is too short.");
        }

        var byteCount = response[1];
        var expectedByteCount = count * 2;

        if (byteCount != expectedByteCount || response.Length != expectedByteCount + 2)
        {
            throw new IOException(
                $"The Modbus register response length is invalid. Expected={expectedByteCount}; ByteCount={byteCount}; Actual={response.Length - 2}.");
        }

        var values = new ushort[count];

        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16BigEndian(
                response.AsSpan(2 + index * 2, 2));
        }

        return values;
    }

    private async Task<byte[]> ExecuteReadAsync(
        byte functionCode,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeoutMs);

            await EnsureConnectedAsync(timeout.Token).ConfigureAwait(false);

            var transactionId = unchecked(++_transactionId);
            var request = BuildReadRequest(
                transactionId,
                functionCode,
                startAddress,
                count);

            try
            {
                await _stream!.WriteAsync(
                    request,
                    0,
                    request.Length,
                    timeout.Token).ConfigureAwait(false);

                await _stream.FlushAsync(timeout.Token).ConfigureAwait(false);

                var header = new byte[7];
                await ReadExactlyAsync(_stream, header, timeout.Token).ConfigureAwait(false);

                var responseTransactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
                var remainingLengthIncludingUnit = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                var responseUnitId = header[6];

                if (responseTransactionId != transactionId)
                {
                    throw new IOException(
                        $"Unexpected Modbus transaction ID. Expected={transactionId}; Actual={responseTransactionId}.");
                }

                if (protocolId != 0)
                {
                    throw new IOException($"Unexpected Modbus protocol ID: {protocolId}.");
                }

                if (responseUnitId != _unitId)
                {
                    throw new IOException(
                        $"Unexpected Modbus unit ID. Expected={_unitId}; Actual={responseUnitId}.");
                }

                if (remainingLengthIncludingUnit is < 2 or > 254)
                {
                    throw new IOException(
                        $"The Modbus response contains an invalid length field: {remainingLengthIncludingUnit}.");
                }

                var pdu = new byte[remainingLengthIncludingUnit - 1];
                await ReadExactlyAsync(_stream, pdu, timeout.Token).ConfigureAwait(false);

                if (pdu.Length == 0)
                {
                    throw new IOException("The Modbus response PDU is empty.");
                }

                if ((pdu[0] & 0x80) != 0)
                {
                    var exceptionCode = pdu.Length > 1 ? pdu[1] : (byte)0;
                    throw new ModbusProtocolException(functionCode, exceptionCode);
                }

                if (pdu[0] != functionCode)
                {
                    throw new IOException(
                        $"Unexpected Modbus function code. Expected=0x{functionCode:X2}; Actual=0x{pdu[0]:X2}.");
                }

                return pdu;
            }
            catch
            {
                DisposeConnection();
                throw;
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_tcpClient is not null && _stream is not null && _tcpClient.Connected)
        {
            return;
        }

        DisposeConnection();

        var client = new TcpClient
        {
            NoDelay = true
        };

        try
        {
            await client.ConnectAsync(_host, _port)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            _tcpClient = client;
            _stream = client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private byte[] BuildReadRequest(
        ushort transactionId,
        byte functionCode,
        ushort startAddress,
        ushort count)
    {
        var request = new byte[12];

        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), 6);
        request[6] = _unitId;
        request[7] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(8, 2), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(10, 2), count);

        return request;
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer,
                offset,
                buffer.Length - offset,
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The Modbus TCP connection was closed before the complete response was received.");
            }

            offset += read;
        }
    }

    private void DisposeConnection()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // Connection cleanup must not mask the original communication error.
        }

        try
        {
            _tcpClient?.Dispose();
        }
        catch
        {
            // Connection cleanup must not mask the original communication error.
        }

        _stream = null;
        _tcpClient = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ReadOnlyModbusTcpClient));
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        DisposeConnection();
        _requestLock.Dispose();

        return ValueTask.CompletedTask;
    }
}

public sealed class ModbusProtocolException : IOException
{
    public ModbusProtocolException(byte functionCode, byte exceptionCode)
        : base(
            $"Modbus exception response. Function=0x{functionCode:X2}; " +
            $"Exception=0x{exceptionCode:X2} ({Describe(exceptionCode)}).")
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }

    public byte FunctionCode { get; }

    public byte ExceptionCode { get; }

    private static string Describe(byte code)
    {
        return code switch
        {
            0x01 => "Illegal function",
            0x02 => "Illegal data address",
            0x03 => "Illegal data value",
            0x04 => "Server device failure",
            0x05 => "Acknowledge",
            0x06 => "Server device busy",
            0x08 => "Memory parity error",
            0x0A => "Gateway path unavailable",
            0x0B => "Gateway target failed to respond",
            _ => "Unknown exception"
        };
    }
}
