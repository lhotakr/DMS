SELECT TOP (200)
    wc.Code AS Workcenter,
    op.OrderCode,
    op.ProductCode,

    st.Starttime,
    st.Endtime,

    state.Name AS State,
    state.Description,
    state.CategoryName,
    state.IsSetup,
    state.IsBreak,
    state.IsCauselessFailure,

    st.CustomText

FROM ana.FactMdaState st

LEFT JOIN ana.FactMdaMes mes
    ON mes.ID = st.MesID

LEFT JOIN ana.DimMdaState state
    ON state.ID = st.StateID

LEFT JOIN ana.DimWorkcenter wc
    ON wc.ID = mes.WorkcenterID

LEFT JOIN ana.DimMdaOperation op
    ON op.ID = mes.OperationID

ORDER BY st.Starttime DESC;