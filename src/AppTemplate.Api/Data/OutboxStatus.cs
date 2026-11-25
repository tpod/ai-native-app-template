namespace AppTemplate.Api.Data;

/// <summary>A delivery row's state. Stored as the enum name; the API's JSON writes <c>pending</c>, <c>in_flight</c>, <c>done</c>, <c>dead</c>.</summary>
public enum OutboxStatus
{
    Pending,
    InFlight,
    Done,
    Dead,
}
