-- Read-only catalog checks for the production requisition reporting procedure.
-- Does not execute or modify the procedure or read transaction rows.
SELECT s.name AS schema_name, o.name AS object_name, o.type_desc,
    DATALENGTH(m.definition) AS definition_bytes,
    m.uses_ansi_nulls, m.uses_quoted_identifier
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN sys.sql_modules AS m ON m.object_id = o.object_id
WHERE s.name = N'dbo' AND o.name = N'sp_rep_requisition_register';

SELECT o.name AS table_name, c.column_id, c.name AS column_name,
    t.name AS data_type, c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.columns AS c
JOIN sys.objects AS o ON o.object_id = c.object_id
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE s.name = N'dbo' AND o.name IN
    (N'txn_production_requisition', N'txn_stock_transfer', N'txn_production_planning_req_proc')
ORDER BY o.name, c.column_id;
