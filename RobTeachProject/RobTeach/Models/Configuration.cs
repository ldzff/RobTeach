using System.Collections.Generic;

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a complete configuration for a product, including its name,
    /// a list of trajectories, and transformation parameters.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Gets or sets the name of the product associated with this configuration.
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// Gets or sets the list of trajectories defined for this product configuration.
        /// </summary>
        public List<Trajectory> Trajectories { get; set; } = new List<Trajectory>();

        /// <summary>
        /// Gets or sets the transformation parameters associated with this configuration.
        /// This is a placeholder for future functionality like scaling, offsetting, or rotating
        /// the entire set of trajectories.
        /// </summary>
        public Transform TransformParameters { get; set; } = new Transform();

        /// <summary>
        /// Initializes a new instance of the <see cref="Configuration"/> class.
        /// A parameterless constructor is provided for JSON deserialization and typical instantiation.
        /// Trajectories list and TransformParameters are initialized to default instances.
        /// </summary>
        public Configuration()
        {
            // Default constructor.
        }
    }
}
