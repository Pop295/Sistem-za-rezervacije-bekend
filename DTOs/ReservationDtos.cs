namespace Bekend.DTOs;

public class ReservationDto
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ReservationCreateDto
{
    public int TimeSlotId { get; set; }
}

public class ReservationStatusUpdateDto
{
    public string Status { get; set; } = string.Empty;
}