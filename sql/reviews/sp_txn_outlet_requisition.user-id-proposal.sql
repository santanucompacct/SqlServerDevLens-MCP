-- REVIEW ONLY: generates a proposed definition; NEVER executes it.
-- This narrow correction does NOT repair the confirmed header/detail mismatch.
-- Do not deploy the returned definition until the full schema/API contract is fixed.
-- Run manually in SSMS in the intended database with VIEW DEFINITION permission.
-- Save/review the returned text privately: production source may contain identifiers.

DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.sp_txn_outlet_requisition', N'P'));
IF @definition IS NULL
    THROW 51000, 'Procedure definition unavailable. Check database, permissions and encryption.', 1;

DECLARE @old nvarchar(200) = N'where user_id=@user_id)';
DECLARE @new nvarchar(200) = N'where user_id=@ops_user_id)';
DECLARE @position int = CHARINDEX(@old COLLATE Latin1_General_100_BIN2, @definition COLLATE Latin1_General_100_BIN2);

-- Fail closed if the reviewed source pattern changed or is ambiguous.
IF @position = 0 OR CHARINDEX(@old COLLATE Latin1_General_100_BIN2,
    @definition COLLATE Latin1_General_100_BIN2, @position + LEN(@old)) > 0
    THROW 51001, 'Reviewed user-ID pattern is missing or ambiguous. Re-read the current procedure.', 1;

DECLARE @branch int = CHARINDEX(N'get_req_cut_off_time_for_req', @definition);
DECLARE @next_branch int = CHARINDEX(N'get_unsettled', @definition, @branch + 1);
IF @branch = 0 OR @next_branch = 0 OR @position <= @branch OR @position >= @next_branch
    THROW 51002, 'Reviewed user-ID pattern is outside the expected branch. Re-review required.', 1;

-- The branch assigns @ops_user_id after sp_Hash2ID; @user_id is never assigned.
-- Return source for review, preserving its existing CREATE/ALTER header and all
-- other content. A deployment script must be prepared separately after review.
SELECT STUFF(@definition, @position, LEN(@old), @new) AS proposed_definition;
