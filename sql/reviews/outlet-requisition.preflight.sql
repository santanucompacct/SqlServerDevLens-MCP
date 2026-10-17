-- Read-only review script. Run in the intended database in SSMS.
-- Does not execute or alter the stored procedure or modify application rows.

SELECT s.name AS schema_name, o.name AS object_name, o.type_desc
FROM sys.objects AS o
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE s.name = N'dbo' AND o.name IN
    (N'txn_outlet_requisition', N'txn_outlet_requisition_detail',
     N'txn_outlet_requisition_flight_detail', N'sp_txn_outlet_requisition')
ORDER BY o.name;

SELECT o.name AS table_name, c.column_id, c.name AS column_name,
    t.name AS data_type, c.max_length, c.precision, c.scale,
    c.is_nullable, c.is_identity
FROM sys.columns AS c
JOIN sys.objects AS o ON o.object_id = c.object_id
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE s.name = N'dbo' AND o.name IN
    (N'txn_outlet_requisition', N'txn_outlet_requisition_detail',
     N'txn_outlet_requisition_flight_detail')
ORDER BY o.name, c.column_id;

-- The supplied procedure refers to these columns on the header table.
-- Any returned row confirms that the old procedure and current header disagree.
SELECT expected.column_name AS missing_header_column
FROM (VALUES (N'product_id'), (N'pri_unit_code'), (N'sec_unit_code'),
    (N'qty'), (N'actual_order_qty'), (N'rate'), (N'amount'), (N'category')) AS expected(column_name)
WHERE NOT EXISTS
(
    SELECT 1 FROM sys.columns AS c
    JOIN sys.objects AS o ON o.object_id = c.object_id
    JOIN sys.schemas AS s ON s.schema_id = o.schema_id
    WHERE s.name = N'dbo' AND o.name = N'txn_outlet_requisition'
      AND c.name = expected.column_name
);

-- A NULL definition can mean missing VIEW DEFINITION permission or encryption.
SELECT s.name AS schema_name, o.name AS procedure_name,
    DATALENGTH(m.definition) AS definition_bytes,
    m.uses_ansi_nulls, m.uses_quoted_identifier
FROM sys.sql_modules AS m
JOIN sys.objects AS o ON o.object_id = m.object_id
JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE s.name = N'dbo' AND o.name = N'sp_txn_outlet_requisition';
