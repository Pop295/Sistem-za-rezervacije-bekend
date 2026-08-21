namespace Bekend.DTOs;

public class TimeSlotDto
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}