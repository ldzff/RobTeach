using Newtonsoft.Json;
using RobTeach.Models;
using System.IO;

namespace RobTeach.Services
{
    public class ConfigurationService
    {
        public void SaveConfiguration(Configuration config, string filePath)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public Configuration LoadConfiguration(string filePath)
        {
            if (!File.Exists(filePath))
            {
                // Or throw an exception / return null and handle in UI
                return null;
            }
            string json = File.ReadAllText(filePath);
            // It's good practice to handle potential deserialization errors
            try
            {
                return JsonConvert.DeserializeObject<Configuration>(json);
            }
            catch (JsonException ex)
            {
                // Log error or rethrow with more context
                // For now, returning null to indicate failure
                System.Diagnostics.Debug.WriteLine($"Error deserializing configuration: {ex.Message}");
                return null;
            }
        }
    }
}
