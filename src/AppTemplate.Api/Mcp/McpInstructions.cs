namespace AppTemplate.Api.Mcp;

/// <summary>The MCP <c>instructions</c> field: how an agent must use this server (spec §7.6).</summary>
public static class McpInstructions
{
    public const string Text =
        """
        This server exposes the app's actions as tools. Every change follows preview -> confirm -> execute.

        1. Preview: call preview_{action} with the input. It saves nothing and returns summary, changes,
           sideEffects, warnings, previewToken and expiresAt.
        2. Confirm: show the user the preview summary and changes (and any warnings) and get their explicit
           confirmation. Never execute without it.
        3. Execute: call {action} with the same input plus previewToken. The token binds the exact input and
           change set, is valid for 300 seconds, and can be used once.

        If execute returns preview.stale, the data changed since the preview: call preview_{action} again and
        show the user the new preview. On preview.expired, preview again. On preview.already_executed, do not
        retry; query the resource to confirm the earlier result.

        Query tools (readOnlyHint) read data and need no preview or confirmation.

        Successful results are always a JSON object in structuredContent (and the same JSON in the text block).
        A tool that returns a list puts it under "items", for example {"items": [...]}; a tool that returns a
        single scalar puts it under "value".

        Errors come back with isError = true as RFC 9457 ProblemDetails JSON: type, title, status, detail,
        instance, plus code (stable, for example note.not_found or validation.failed), details, suggestion
        (what to do next) and traceId (quote it when reporting a problem). Act on code and suggestion; do not
        parse detail. Call tools/list to see the available tools and the error codes each one can return.
        """;
}
