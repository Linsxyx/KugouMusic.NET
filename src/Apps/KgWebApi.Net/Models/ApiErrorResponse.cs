namespace KgWebApi.Net.Models;

public sealed record ApiErrorResponse(int Status, string Msg, int ErrorCode);
