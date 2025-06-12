using System.Collections.Generic;
using System.Windows; // For Point

namespace RobTeach.Models
{
    public class Trajectory
    {
        public string OriginalEntityHandle { get; set; } // To identify the source CAD entity
        public string EntityType { get; set; } // E.g., "Line", "Arc", "LwPolyline"
        public List<Point> Points { get; set; } = new List<Point>();
        public int NozzleNumber { get; set; }
        public bool IsWater { get; set; } // true for water, false for air

        // Default constructor for JSON deserialization
        public Trajectory() { }
    }
}
