using System;
using System.Collections.Generic;

namespace Bekend.Models;

public partial class TimeSlot
{
    public int Id { get; set; }

    public int ServiceId { get; set; }

    public int TableNumber { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public bool? IsAvailable { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Reservation? Reservation { get; set; }

    public virtual Service Service { get; set; } = null!;
}
