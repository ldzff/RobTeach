using Newtonsoft.Json;
using RobTeach.Models;
using System.IO;
using System.Diagnostics; // For Debug.WriteLine
using System; // Added for ArgumentNullException

namespace RobTeach.Services
{
    /// <summary>
    /// Provides services for saving and loading application configurations.
    /// Configurations are serialized to and deserialized from JSON format.
    /// </summary>
    public class ConfigurationService
    {
        /// <summary>
        /// Saves the provided application <see cref="Configuration"/> to a JSON file.
        /// </summary>
        /// <param name="config">The <see cref="Configuration"/> object to save.</param>
        /// <param name="filePath">The path to the file where the configuration will be saved.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="config"/> or <paramref name="filePath"/> is null.</exception>
        /// <exception cref="System.Exception">Thrown if an error occurs during JSON serialization or file writing.
        /// Specific exceptions can include <see cref="JsonException"/> or <see cref="IOException"/>.</exception>
        public void SaveConfiguration(Configuration config, string filePath)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            // Serialize the configuration object to a JSON string with indentation for readability.
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            // Write the JSON string to the specified file path.
            // This will overwrite the file if it already exists.
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Loads an application <see cref="Configuration"/> from a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the file from which the configuration will be loaded.</param>
        /// <returns>A <see cref="Configuration"/> object if deserialization is successful;
        /// otherwise, <c>null</c> if the file does not exist or if an error occurs during deserialization.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
        public Configuration LoadConfiguration(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
            {
                // File not found, return null. UI should handle this.
                Debug.WriteLine($"[ConfigurationService] Configuration file not found: {filePath}");
                return null;
            }

            string json = File.ReadAllText(filePath);

            try
            {
                // Deserialize the JSON string back into a Configuration object.
                return JsonConvert.DeserializeObject<Configuration>(json);
            }
            catch (JsonException ex)
            {
                // Log the deserialization error for debugging purposes.
                Debug.WriteLine($"[ConfigurationService] Error deserializing configuration from {filePath}: {ex.Message}");
                // Return null to indicate failure to the caller, which should handle it gracefully.
                return null;
            }
            catch (Exception ex) // Catch other potential errors during file read or deserialization setup
            {
                Debug.WriteLine($"[ConfigurationService] Unexpected error loading configuration from {filePath}: {ex.ToString()}");
                return null;
            }
        }
    }
}
