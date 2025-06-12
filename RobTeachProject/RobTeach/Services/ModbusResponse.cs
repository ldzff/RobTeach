namespace RobTeach.Services
{
    public class ModbusResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public ModbusResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static ModbusResponse Ok(string message = "Operation successful.") => new ModbusResponse(true, message);
        public static ModbusResponse Fail(string message) => new ModbusResponse(false, message);
    }
}
