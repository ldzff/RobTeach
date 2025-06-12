using System.Collections.Generic;

namespace RobTeach.Models
{
    public class Configuration
    {
        public string ProductName { get; set; }
        public List<Trajectory> Trajectories { get; set; } = new List<Trajectory>();
        public Transform TransformParameters { get; set; } = new Transform();

        // Default constructor for JSON deserialization
        public Configuration() { }
    }
}
