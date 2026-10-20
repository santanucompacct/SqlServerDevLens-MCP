using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Runtime;

public sealed class CapturedResultTools(CapturedResults? results)
{
    public IEnumerable<McpServerTool> CreateTools() =>
    [
        McpServerTool.Create(Read, new() { Name = "read_result_page", Description = "Read immutable captured SQL/procedure or masked Postman JSON from ONE prior execution. resultId comes from Capture.ResultId. maxCharacters 256-128000 default 32000; follow nextOffset using expectedRevision until complete. Concatenate Json text pages then parse the full JSON; a page alone may not be valid JSON. No SQL or HTTP is re-executed. Expires after 15 minutes, release, or Stop. Keep result data private." }),
        McpServerTool.Create(Release, new() { Name = "release_result", Description = "Release a captured result from this desktop session's memory. No database or Postman change. Read all needed pages first; cannot recover a released result without a new execution/approval." })
    ];
    public CapturedResultPage Read(string resultId, int offset = 0, int maxCharacters = 32000, string? expectedRevision = null)
    {
        try { return (results ?? throw new InvalidOperationException("No result capture session is running.")).Read(resultId, offset, maxCharacters, expectedRevision); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { throw new McpException(e.Message); }
    }
    public object Release(string resultId)
    {
        try { (results ?? throw new InvalidOperationException("No result capture session is running.")).Release(resultId); return new { released = true }; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { throw new McpException(e.Message); }
    }
}
