using System.Collections.Generic;
using System.Windows; // For Point

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a single trajectory derived from a CAD entity, including its points and application parameters.
    /// </summary>
    public class Trajectory
    {
        /// <summary>
        /// Gets or sets the handle of the original CAD entity from which this trajectory was derived.
        /// This helps in linking the trajectory back to its source in the DXF document.
        /// </summary>
        public string OriginalEntityHandle { get; set; }

        /// <summary>
        /// Gets or sets the type of the original CAD entity (e.g., "Line", "Arc", "LwPolyline").
        /// </summary>
        public string EntityType { get; set; }

        /// <summary>
        /// Gets or sets the list of points that define the path of the trajectory.
        /// These points are typically in the coordinate system intended for the robot.
        /// </summary>
        public List<Point> Points { get; set; } = new List<Point>();

        /// <summary>
        /// Gets or sets the nozzle number to be used for this trajectory.
        /// </summary>
        public int NozzleNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether water spray is used for this trajectory.
        /// True indicates water spray; false indicates air spray (or other non-water medium).
        /// </summary>
        public bool IsWater { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Trajectory"/> class.
        /// A parameterless constructor is required for JSON deserialization.
        /// </summary>
        public Trajectory()
        {
            // Default constructor for JSON deserialization and typical instantiation.
            // Points list is initialized by default.
        }
    }
}
