# Outlet requisition review — 2026-10-17

Read-only inspection through the installed DevLens stdio adapter verified all
four requested dbo objects. Structures and three recent rows per table were
read successfully without truncation. The complete procedure was retrieved in
1900-character chunks and reassembled to its verified 29,457-character length.
No procedure was executed and no database data/schema was changed. Native chat
tool discovery still exposed status only during this check; adapter calls do
not prove the chat's tool list has refreshed.

The full production definition remains in a local private review copy, outside
this Git repository. No customer rows, credentials, production identifier
hashes or unreviewed procedure dump are published here.

## Confirmed issues

1. **Header/detail schema mismatch.** The header has 25 columns, detail 15 and
   flight detail 19. The procedure's create/update branches still insert
   product_id, pri_unit_code, sec_unit_code, qty, actual_order_qty, rate,
   amount and category into the header; all eight are absent there. Seven
   of these exist on the detail table; category is absent from all three
   inspected tables. Product browse/edit branches also read missing header
   fields. Neither detail table is referenced anywhere in this procedure.
   This is a compatibility defect established by source and catalog inspection,
   not by executing those branches. Do not blindly move the INSERT: the header
   requires delivery_schedue_date, and detail/flight mappings, identity linkage,
   category semantics and API JSON contracts need confirmation.
2. **Transaction cleanup.** Seven CATCH blocks in read-oriented report branches
   test @@TRANCOUNT then SELECT an error without rollback. A failure after BEGIN
   TRANSACTION can leave the transaction open; if the transaction count is zero,
   the conditional SELECT can suppress the response. The two write-branch
   CATCH blocks do roll back, but blanket rollback can also affect an outer
   caller transaction. Review ownership/savepoint behavior and sp_Hash2ID's
   behavior before choosing a consistent transaction pattern. No failure was
   intentionally triggered on production.
3. **Wrong user variable.** get_req_cut_off_time_for_req assigns the hash result
   to @ops_user_id, then filters using @user_id, which is declared but never
   assigned in the procedure. This prevents the intended user-role lookup.
   The proposal script generates only this narrow correction for review.
4. **Edit branch initialization.** update_outlet_requisition inserts
   @created_cc_id without assigning it in that branch. The header allows NULL,
   so the intended audit preservation/derivation needs a business decision.

Every inspected table has an identity txn_id primary key. No foreign-key target
was returned for any column. Shared doc_no and requisition_detail_txn_id suggest
logical relationships, but these should not be described as enforced foreign
keys or changed without validating historical data and the write workflow.

## Files and next decision

- outlet-requisition.preflight.sql reproduces the catalog checks, read-only.
- sp_txn_outlet_requisition.user-id-proposal.sql generates a proposed definition
  with the wrong-variable correction, using exact-pattern/branch checks. It does
  not execute or deploy it, and is not a complete compatibility fix.

Confirm that this is the procedure currently called by the application and
provide the expected create/edit JSON contracts (fake data only). Then prepare
the complete header/detail/flight refactor as a separate reviewed SQL change
and validate it in a disposable/test database before any production deployment.

## Follow-up: sp_rep_requisition_register

At the user's request, the complete 25,628-character definition of
dbo.sp_rep_requisition_register was also read through the adapter in bounded
chunks and retained only in the private local review directory. It handles
inter-department **production** requisitions, not outlet requisitions. Its main
sources are dbo.txn_production_requisition, dbo.txn_stock_transfer and
dbo.txn_production_planning_req_proc; it never references the three outlet
tables. Catalog checks confirmed the main transaction-table columns used in
the inspected joins/projections are present. This is source/catalog inspection,
not proof of successful execution or business correctness.

The procedure exposes twelve report modes covering dashboard/register/detail,
planning/department/product summaries, issue history, source/BOM tracing,
alerts and department selection/list/detail. It derives pending/partial/full
status from requested and issued quantities and matches transfers in the
reverse route. It reads business tables and builds temporary working tables;
no business-table INSERT/UPDATE/DELETE or nested procedure execution was found.
The current SELECT-only DevLens tool intentionally does not execute it.

No change to this report procedure is proposed from this inspection alone.
Confirm outlet versus production workflow and the specific expected result
before preparing a behavior-changing SQL patch. The legacy outlet findings
above must not be applied to this separate reporting procedure.
