namespace Bekend.DTOs;

public class ReservationDto
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int TableNumber { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}


public class ReservationCreateDto
{
    public int ServiceId { get; set; }
    public int TableNumber { get; set; }
    public string Date { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
}

public class ReservationStatusUpdateDto
{
    public string Status { get; set; } = string.Empty;
}