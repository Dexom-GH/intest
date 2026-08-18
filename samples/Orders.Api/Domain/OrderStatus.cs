namespace Orders.Api.Domain;

public enum OrderStatus
{
    Draft = 0,
    Placed = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}