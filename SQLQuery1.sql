SELECT TOP (100)
    mes.Starttime,
    mes.Endtime,

    wc.Code AS Workcenter,
    wc.Description AS WorkcenterDescription,
    wc.PlantName,

    op.OrderCode,
    op.OperationCode,
    op.OperationDescription,

    op.ProductCode,
    op.ProductDescription,

    op.OrderQuantity,

    mes.PerformanceTotal,
    mes.PerformanceGood,
    mes.PerformanceBad,
    mes.PerformanceRework,

    mes.DurationUtilization,
    mes.DurationDown

FROM ana.FactMdaMes mes

LEFT JOIN ana.DimWorkcenter wc
    ON wc.ID = mes.WorkcenterID

LEFT JOIN ana.DimMdaOperation op
    ON op.ID = mes.OperationID

ORDER BY mes.Starttime DESC;