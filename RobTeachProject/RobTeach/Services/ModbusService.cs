using EasyModbus;
using RobTeach.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics; // Added for Debug.WriteLine
using System.Linq;

namespace RobTeach.Services
{
    public class ModbusService
    {
        private ModbusClient modbusClient;
        // Register definitions based on README
        private const int TrajectoryCountRegister = 1000;
        private const int BasePointsCountRegister = 1001;
        private const int BaseXCoordsRegister = 1002;
        private const int BaseYCoordsRegister = 1052;
        private const int BaseNozzleNumRegister = 1102;
        private const int BaseSprayTypeRegister = 1103;

        private const int TrajectoryRegisterOffset = 100;
        private const int MaxPointsPerTrajectory = 50;
        private const int MaxTrajectories = 5;

        public ModbusResponse Connect(string ipAddress, int port)
        {
            try
            {
                modbusClient = new ModbusClient(ipAddress, port);
                modbusClient.ConnectionTimeout = 2000; // 2 seconds
                // For EasyModbus, ReceiveTimeout and SendTimeout are properties of the ModbusClient instance.
                // However, some versions might not expose these directly or they are handled by ConnectionTimeout.
                // If specific Send/Receive timeouts are needed and not directly on ModbusClient,
                // they might be part of underlying socket if accessible, or not configurable.
                // For now, assuming ConnectionTimeout covers the initial connection phase.
                // Read/Write operations in EasyModbus typically have their own internal timeouts or use a default.
                // Let's ensure we're using properties that exist. If ReceiveTimeout/SendTimeout aren't members,
                // we'd omit them or find the correct way for the library version.
                // Assuming they are NOT standard on the base ModbusClient, will rely on ConnectionTimeout and default operation timeouts.

                modbusClient.Connect();
                if (modbusClient.Connected)
                {
                    return ModbusResponse.Ok($"Successfully connected to {ipAddress}:{port}.");
                }
                else
                {
                    // This case might be rare if Connect() throws on failure
                    return ModbusResponse.Fail("Connection failed: Unknown reason after Connect() call.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModbusService] Connection error: {ex.ToString()}");
                return ModbusResponse.Fail($"Connection error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (modbusClient != null && modbusClient.Connected)
            {
                try
                {
                    modbusClient.Disconnect();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ModbusService] Disconnect error: {ex.ToString()}");
                    // Optionally, can decide if UI needs to know about disconnect errors. Usually, we assume it succeeds.
                }
            }
            // Ensure client is nullified or state is reset even if Disconnect throws,
            // though EasyModbus might handle its internal state.
            // modbusClient = null; // Or handle Connected state more explicitly
        }

        public bool IsConnected => modbusClient != null && modbusClient.Connected;

        public ModbusResponse SendConfiguration(Configuration config)
        {
            if (!IsConnected) return ModbusResponse.Fail("Error: Not connected to Modbus server.");
            if (config == null || config.Trajectories == null || !config.Trajectories.Any())
                return ModbusResponse.Fail("Error: No configuration or trajectories to send.");

            try
            {
                int trajectoriesToSendCount = Math.Min(config.Trajectories.Count, MaxTrajectories);

                // It's often safer to write one piece of data at a time and check status,
                // but for simplicity, we group writes. EasyModbus typically throws on failure.
                modbusClient.WriteSingleRegister(TrajectoryCountRegister, trajectoriesToSendCount);

                for (int i = 0; i < trajectoriesToSendCount; i++)
                {
                    var traj = config.Trajectories[i];
                    int pointsInCurrentTraj = Math.Min(traj.Points.Count, MaxPointsPerTrajectory);

                    int currentTrajBasePointsCountReg = BasePointsCountRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajBasePointsCountReg, pointsInCurrentTraj);

                    if (pointsInCurrentTraj > 0)
                    {
                        int[] xCoords = traj.Points.Take(pointsInCurrentTraj).Select(p => (int)Math.Round(p.X)).ToArray();
                        int[] yCoords = traj.Points.Take(pointsInCurrentTraj).Select(p => (int)Math.Round(p.Y)).ToArray();

                        int currentTrajBaseXReg = BaseXCoordsRegister + (i * TrajectoryRegisterOffset);
                        modbusClient.WriteMultipleRegisters(currentTrajBaseXReg, xCoords);

                        int currentTrajBaseYReg = BaseYCoordsRegister + (i * TrajectoryRegisterOffset);
                        modbusClient.WriteMultipleRegisters(currentTrajBaseYReg, yCoords);
                    }

                    int currentTrajNozzleReg = BaseNozzleNumRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajNozzleReg, traj.NozzleNumber);

                    int currentTrajSprayTypeReg = BaseSprayTypeRegister + (i * TrajectoryRegisterOffset);
                    modbusClient.WriteSingleRegister(currentTrajSprayTypeReg, traj.IsWater ? 1 : 0);
                }
                return ModbusResponse.Ok($"Successfully sent {trajectoriesToSendCount} trajectories.");
            }
            // Catch specific Modbus/IO exceptions if the library provides them and they are distinct.
            // For EasyModbus, often it's System.IO.IOException or a general Exception for many comms issues.
            catch (System.IO.IOException ioEx)
            {
                 Debug.WriteLine($"[ModbusService] IO error during send: {ioEx.ToString()}");
                 return ModbusResponse.Fail($"IO error sending Modbus data: {ioEx.Message}");
            }
            catch (EasyModbus.Exceptions.ModbusException modEx) // Example if EasyModbus has its own base exception
            {
                 Debug.WriteLine($"[ModbusService] Modbus protocol error during send: {modEx.ToString()}");
                 return ModbusResponse.Fail($"Modbus protocol error: {modEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModbusService] General error during send: {ex.ToString()}");
                return ModbusResponse.Fail($"Error sending Modbus data: {ex.Message}");
            }
        }
    }
}
