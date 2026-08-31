SELECT TOP (200)
    wc.Code AS Workcenter,
    op.OrderCode,
    op.ProductCode,

    c.Name AS Counter,
    c.Description,
    f.Timestamp,
    f.Value,
    f.CustomText

FROM ana.FactMdaCounter f

LEFT JOIN ana.FactMdaMes mes
    ON mes.ID = f.MesID

LEFT JOIN ana.DimMdaCounter c
    ON c.ID = f.CounterID

LEFT JOIN ana.DimWorkcenter wc
    ON wc.ID = mes.WorkcenterID

LEFT JOIN ana.DimMdaOperation op
    ON op.ID = mes.OperationID

ORDER BY f.Timestamp DESC;