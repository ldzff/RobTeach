using EasyModbus;
using RobTeach.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace RobTeach.Services
{
    /// <summary>
    /// Provides services for communicating with a Modbus TCP server (e.g., a robot controller).
    /// Handles connection, disconnection, and sending configuration data.
    /// </summary>
    public class ModbusService
    {
        private ModbusClient modbusClient; // The EasyModbus client instance

        // Define Modbus register addresses based on the application's README or device specification.
        // These constants define the memory map on the Modbus server (robot).
        private const int TrajectoryCountRegister = 1000;   // Register to write the number of trajectories being sent.
        private const int BasePointsCountRegister = 1001;   // Base register for the point count of the first trajectory.
        private const int BaseXCoordsRegister = 1002;       // Base register for X coordinates of the first trajectory.
        private const int BaseYCoordsRegister = 1052;       // Base register for Y coordinates of the first trajectory.
        private const int BaseNozzleNumRegister = 1102;     // Base register for nozzle number of the first trajectory.
        private const int BaseSprayTypeRegister = 1103;     // Base register for spray type of the first trajectory.

        private const int TrajectoryRegisterOffset = 100;   // Offset between base registers of consecutive trajectories.
        private const int MaxPointsPerTrajectory = 50;      // Maximum number of points per trajectory supported by the robot.
        private const int MaxTrajectories = 5;              // Maximum number of trajectories supported by the robot.

        /// <summary>
        /// Attempts to connect to the Modbus TCP server at the specified IP address and port.
        /// </summary>
        /// <param name="ipAddress">The IP address of the Modbus server.</param>
        /// <param name="port">The port number of the Modbus server.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating the success or failure of the connection attempt.</returns>
        public ModbusResponse Connect(string ipAddress, int port)
        {
            try
            {
                modbusClient = new ModbusClient(ipAddress, port);
                modbusClient.ConnectionTimeout = 2000; // Set connection timeout to 2 seconds.

                // Note: EasyModbus typically handles send/receive timeouts internally for its operations.
                // If more granular control is needed, it might require a different library or direct socket manipulation.

                modbusClient.Connect(); // Attempt to establish the connection.
                if (modbusClient.Connected)
                {
                    return ModbusResponse.Ok($"Successfully connected to Modbus server at {ipAddress}:{port}.");
                }
                else
                {
                    // This path might be less common if Connect() throws an exception on failure.
                    return ModbusResponse.Fail("Connection failed: ModbusClient reported not connected after Connect() call.");
                }
            }
            catch (Exception ex) // Catch any exception during connection (e.g., network issues, server not responding).
            {
                Debug.WriteLine($"[ModbusService] Connection error to {ipAddress}:{port}: {ex.ToString()}");
                return ModbusResponse.Fail($"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnects from the Modbus TCP server if a connection is active.
        /// </summary>
        public void Disconnect()
        {
            if (modbusClient != null && modbusClient.Connected)
            {
                try
                {
                    modbusClient.Disconnect();
                }
                catch (Exception ex) // Catch potential errors during disconnection.
                {
                    Debug.WriteLine($"[ModbusService] Disconnect error: {ex.ToString()}");
                    // Depending on requirements, this error might be surfaced to the UI or just logged.
                }
                // modbusClient = null; // Optionally nullify for garbage collection, though IsConnected will be false.
            }
        }

        /// <summary>
        /// Gets a value indicating whether the Modbus client is currently connected.
        /// </summary>
        public bool IsConnected => modbusClient != null && modbusClient.Connected;

        /// <summary>
        /// Sends the specified robot <see cref="Configuration"/> (trajectories and parameters) to the connected Modbus server.
        /// </summary>
        /// <param name="config">The <see cref="Configuration"/> to send.</param>
        /// <returns>A <see cref="ModbusResponse"/> indicating the success or failure of the send operation.</returns>
        public ModbusResponse SendConfiguration(Models.Configuration config)
        {
            if (!IsConnected) return ModbusResponse.Fail("Error: Not connected to Modbus server. Please connect first.");
            if (config == null || config.Trajectories == null || !config.Trajectories.Any())
                return ModbusResponse.Fail("Error: No configuration data or trajectories available to send.");

            try
            {
                // Determine how many trajectories to send, respecting the robot's maximum limit.
                int trajectoriesToSendCount = Math.Min(config.Trajectories.Count, MaxTrajectories);

                // Write the total number of trajectories being sent.
                modbusClient.WriteSingleRegister(TrajectoryCountRegister, trajectoriesToSendCount);

                // Loop through each trajectory to be sent.
                for (int i = 0; i < trajectoriesToSendCount; i++)
                {
                    var traj = config.Trajectories[i];
                    // Determine how many points to send for the current trajectory, respecting the per-trajectory limit.
                    int pointsInCurrentTraj = Math.Min(traj.Points.Count, MaxPointsPerTrajectory);

                    // Calculate the base register for the current trajectory's point count.
                    int currentTrajBasePointsCountReg = BasePointsCountRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajBasePointsCountReg, pointsInCurrentTraj);

                    if (pointsInCurrentTraj > 0)
                    {
                        // Convert trajectory points (doubles) to integers for Modbus registers.
                        // This assumes the robot expects integer coordinates. Scaling might be needed if units differ (e.g., mm vs 0.1mm).
                        int[] xCoords = traj.Points.Take(pointsInCurrentTraj).Select(p => (int)Math.Round(p.X)).ToArray();
                        int[] yCoords = traj.Points.Take(pointsInCurrentTraj).Select(p => (int)Math.Round(p.Y)).ToArray();

                        // Calculate base registers for X and Y coordinates of the current trajectory.
                        int currentTrajBaseXReg = BaseXCoordsRegister + (i * TrajectoryRegisterOffset);
                        modbusClient.WriteMultipleRegisters(currentTrajBaseXReg, xCoords);

                        int currentTrajBaseYReg = BaseYCoordsRegister + (i * TrajectoryRegisterOffset);
                        modbusClient.WriteMultipleRegisters(currentTrajBaseYReg, yCoords);
                    }

                    // Write nozzle number for the current trajectory.
                    int currentTrajNozzleReg = BaseNozzleNumRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajNozzleReg, traj.NozzleNumber);

                    // Write spray type (0 for air, 1 for water) for the current trajectory.
                    int currentTrajSprayTypeReg = BaseSprayTypeRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajSprayTypeReg, traj.IsWater ? 1 : 0);
                }
                return ModbusResponse.Ok($"Successfully sent {trajectoriesToSendCount} trajectories to Modbus server.");
            }
            // Handle specific exceptions from the Modbus library if they are known and provide distinct information.
            catch (System.IO.IOException ioEx) // Often indicates network or stream-related issues.
            {
                 Debug.WriteLine($"[ModbusService] IO error during send: {ioEx.ToString()}");
                 return ModbusResponse.Fail($"IO error sending Modbus data: {ioEx.Message}");
            }
            catch (EasyModbus.Exceptions.ModbusException modEx) // Catch specific Modbus protocol errors.
            {
                 Debug.WriteLine($"[ModbusService] Modbus protocol error during send: {modEx.ToString()}");
                 return ModbusResponse.Fail($"Modbus protocol error: {modEx.Message}");
            }
            catch (Exception ex) // Catch any other unexpected errors during the send operation.
            {
                Debug.WriteLine($"[ModbusService] General error during send: {ex.ToString()}");
                return ModbusResponse.Fail($"An unexpected error occurred while sending Modbus data: {ex.Message}");
            }
        }
    }
}
