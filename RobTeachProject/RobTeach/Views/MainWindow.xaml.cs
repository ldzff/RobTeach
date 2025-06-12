using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
// Explicitly using System.Windows.Shapes.Shape to avoid ambiguity
// using System.Windows.Shapes; // This line can be removed if all Shape usages are qualified
using Microsoft.Win32;
using RobTeach.Services;
using RobTeach.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using netDxf;
using netDxf.Entities;
using System.IO;
// using System.Windows.Threading; // Was for optional Dispatcher.Invoke, not currently used.
// using System.Text.RegularExpressions; // Was for optional IP validation, not currently used.

namespace RobTeach.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml. This is the main window of the RobTeach application,
    /// handling UI events, displaying CAD data, managing configurations, and initiating Modbus communication.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Services used by the MainWindow
        private readonly CadService _cadService = new CadService();
        private readonly ConfigurationService _configService = new ConfigurationService();
        private readonly ModbusService _modbusService = new ModbusService();

        // Current state variables
        private DxfDocument _currentDxfDocument; // Holds the currently loaded DXF document object.
        private string _currentDxfFilePath;      // Path to the currently loaded DXF file.
        private string _currentLoadedConfigPath; // Path to the last successfully loaded configuration file.
        private Models.Configuration _currentConfiguration; // The active configuration, either loaded or built from selections.

        // Collections for managing DXF entities and their WPF shape representations
        private readonly List<object> _selectedDxfEntities = new List<object>(); // Stores original DXF entities selected by the user.
        // Qualified System.Windows.Shapes.Shape for dictionary key
        private readonly Dictionary<System.Windows.Shapes.Shape, object> _wpfShapeToDxfEntityMap = new Dictionary<System.Windows.Shapes.Shape, object>();
        private readonly Dictionary<string, EntityObject> _dxfEntityHandleMap = new Dictionary<string, EntityObject>(); // Maps DXF entity handles to entities for quick lookup when loading configs.
        private readonly List<System.Windows.Shapes.Polyline> _trajectoryPreviewPolylines = new List<System.Windows.Shapes.Polyline>(); // Keeps track of trajectory preview polylines for easy removal.

        // Fields for CAD Canvas Zoom/Pan functionality
        private ScaleTransform _scaleTransform;         // Handles scaling (zoom) of the canvas content.
        private TranslateTransform _translateTransform; // Handles translation (pan) of the canvas content.
        private TransformGroup _transformGroup;         // Combines scale and translate transforms.
        private System.Windows.Point _panStartPoint;    // Qualified: Stores the starting point of a mouse pan operation.
        private bool _isPanning;                        // Flag indicating if a pan operation is currently in progress.
        private Rect _dxfBoundingBox = Rect.Empty;      // Stores the calculated bounding box of the entire loaded DXF document.

        // Styling constants for visual feedback
        private static readonly Brush DefaultStrokeBrush = Brushes.DarkSlateGray; // Default color for CAD shapes.
        private static readonly Brush SelectedStrokeBrush = Brushes.DodgerBlue;   // Color for selected CAD shapes.
        private const double DefaultStrokeThickness = 1;                          // Default stroke thickness.
        private const double SelectedStrokeThickness = 2.5;                       // Thickness for selected shapes and trajectories.
        private const string TrajectoryPreviewTag = "TrajectoryPreview";          // Tag for identifying trajectory polylines on canvas (not actively used for removal yet).

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// Sets up default values, initializes transformation objects for the canvas,
        /// and attaches necessary mouse event handlers for canvas interaction.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            if (CadCanvas.Background == null) CadCanvas.Background = Brushes.LightGray; // Ensure canvas has a background for hit testing.

            // Initialize product name with a timestamp to ensure uniqueness for new configurations.
            ProductNameTextBox.Text = $"Product_{DateTime.Now:yyyyMMddHHmmss}";
            _currentConfiguration = new Models.Configuration();
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            // Setup transformations for the CAD canvas
            _scaleTransform = new ScaleTransform(1, 1);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            CadCanvas.RenderTransform = _transformGroup;

            // Attach mouse event handlers for canvas zoom and pan
            CadCanvas.MouseWheel += CadCanvas_MouseWheel;
            CadCanvas.MouseDown += CadCanvas_MouseDown; // For initiating pan
            CadCanvas.MouseMove += CadCanvas_MouseMove; // For active panning
            CadCanvas.MouseUp += CadCanvas_MouseUp;     // For ending pan
        }

        /// <summary>
        /// Handles the Closing event of the window. Ensures Modbus connection is disconnected.
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _modbusService.Disconnect(); // Clean up Modbus connection.
        }

        /// <summary>
        /// Handles the Click event of the "Load DXF" button.
        /// Prompts the user to select a DXF file, loads it using <see cref="CadService"/>,
        /// processes its entities for display, and fits the view to the loaded drawing.
        /// </summary>
        private void LoadDxfButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*", Title = "Load DXF File" };
            // Attempt to set a sensible initial directory for the dialog.
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/"; // Fallback if relative path fails.
            openFileDialog.InitialDirectory = initialDir;

            try {
                if (openFileDialog.ShowDialog() == true) { // User selected a file
                    _currentDxfFilePath = openFileDialog.FileName;
                    StatusTextBlock.Text = $"Loading DXF: {Path.GetFileName(_currentDxfFilePath)}...";

                    // Clear previous application state related to DXF and configuration
                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear(); _selectedDxfEntities.Clear();
                    _trajectoryPreviewPolylines.Clear(); _dxfEntityHandleMap.Clear();
                    _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text }; // Reset to a new, unsaved config
                    _currentLoadedConfigPath = null;
                    _currentDxfDocument = null;
                    _dxfBoundingBox = Rect.Empty;
                    UpdateTrajectoryPreview();

                    // Load the DXF document using the service. This might throw exceptions.
                    _currentDxfDocument = _cadService.LoadDxf(_currentDxfFilePath);

                    if (_currentDxfDocument == null) {
                        StatusTextBlock.Text = "Failed to load DXF document (null document returned).";
                        MessageBox.Show("The DXF document could not be loaded. The file might be empty or an unknown error occurred.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Populate a map of entity handles to entities for quick lookup (used when loading configurations).
                    foreach(var entity in _currentDxfDocument.Entities.All) {
                        if (!string.IsNullOrEmpty(entity.Handle)) _dxfEntityHandleMap[entity.Handle] = entity; }

                    // Convert DXF entities to WPF shapes for display.
                    List<System.Windows.Shapes.Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument); // Qualified List<System.Windows.Shapes.Shape>
                    int shapeIndex = 0; // Used to correlate flat list of shapes with iterated DXF entities.
                    // Helper action to map DXF entity to WPF shape, add to canvas, and set up click handling.
                    Action<EntityObject> mapAndAddShape = (dxfEntity) => {
                        if (shapeIndex < wpfShapes.Count && wpfShapes[shapeIndex] != null) {
                            var wpfShape = wpfShapes[shapeIndex]; // Type is System.Windows.Shapes.Shape from list
                            wpfShape.Stroke = DefaultStrokeBrush; wpfShape.StrokeThickness = DefaultStrokeThickness;
                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked; // Enable selection
                            _wpfShapeToDxfEntityMap[wpfShape] = dxfEntity; // Map WPF shape back to DXF entity
                            CadCanvas.Children.Add(wpfShape);
                            shapeIndex++; }};
                    // Process supported entity types using corrected access via .Entities property
                    _currentDxfDocument.Entities.Lines.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.Entities.Arcs.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.Entities.LwPolylines.ToList().ForEach(mapAndAddShape);
                    // TODO: Add other entity types (Circles, Ellipses, Polylines, etc.) if CadService supports them.

                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument); // Calculate overall bounds.
                    PerformFitToView(); // Fit canvas view to the new drawing.
                    StatusTextBlock.Text = $"Loaded: {Path.GetFileName(_currentDxfFilePath)}. Click shapes to select.";
                } else { StatusTextBlock.Text = "DXF loading cancelled."; }
            }
            // Specific error handling for file operations and DXF parsing.
            catch (FileNotFoundException fnfEx) {
                StatusTextBlock.Text = "Error: DXF file not found.";
                MessageBox.Show($"DXF file not found:\n{fnfEx.Message}", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            catch (netDxf.DxfVersionNotSupportedException dxfVerEx) { // Example of specific netDxf exception.
                StatusTextBlock.Text = "Error: DXF version not supported.";
                MessageBox.Show($"The DXF file version is not supported by the parser:\n{dxfVerEx.Message}", "DXF Version Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            catch (Exception ex) { // Catch-all for other unexpected errors.
                StatusTextBlock.Text = "Error loading or processing DXF file.";
                MessageBox.Show($"An error occurred while loading or processing the DXF file:\n{ex.Message}\n\nEnsure the file is a valid DXF format.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
                // Clean up UI and state if loading failed catastrophically.
                CadCanvas.Children.Clear();
                _selectedDxfEntities.Clear(); _wpfShapeToDxfEntityMap.Clear(); _dxfEntityHandleMap.Clear();
                _trajectoryPreviewPolylines?.Clear();
                _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                UpdateTrajectoryPreview();
            }
        }

        /// <summary>
        /// Handles mouse click events on CAD shapes displayed on the canvas.
        /// Toggles the selection state of the clicked DXF entity and updates its visual appearance.
        /// Refreshes the trajectory preview based on the new selection.
        /// </summary>
        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return; // Event already handled (e.g., by panning).
            // Qualified System.Windows.Shapes.Shape for casting sender
            if (sender is System.Windows.Shapes.Shape clickedShape && _wpfShapeToDxfEntityMap.TryGetValue(clickedShape, out object dxfEntity))
            {
                if (_selectedDxfEntities.Contains(dxfEntity))
                {
                    _selectedDxfEntities.Remove(dxfEntity);
                    clickedShape.Stroke = DefaultStrokeBrush;
                    clickedShape.StrokeThickness = DefaultStrokeThickness;
                }
                else
                {
                    _selectedDxfEntities.Add(dxfEntity);
                    clickedShape.Stroke = SelectedStrokeBrush;
                    clickedShape.StrokeThickness = SelectedStrokeThickness;
                }
                UpdateTrajectoryPreview(); // Refresh trajectory display based on new selection.
                e.Handled = true; // Mark that this click event has been processed.
            }
        }

        /// <summary>
        /// Updates the visual preview of trajectories on the CAD canvas.
        /// Clears existing trajectory previews and redraws them based on currently selected DXF entities.
        /// </summary>
        private void UpdateTrajectoryPreview()
        {
            // Remove all previously drawn trajectory polylines.
            _trajectoryPreviewPolylines.ForEach(p => CadCanvas.Children.Remove(p));
            _trajectoryPreviewPolylines.Clear();

            double arcResolutionDegrees = 15.0; // Resolution for discretizing arcs into points.

            // Generate and draw trajectories for each selected DXF entity.
            foreach (object dxfEntity in _selectedDxfEntities)
            {
                List<System.Windows.Point> entityPoints = null; // Qualified List<System.Windows.Point>
                // Convert different DXF entity types to lists of points.
                if (dxfEntity is netDxf.Entities.Line line) entityPoints = _cadService.ConvertLineToPoints(line);
                else if (dxfEntity is netDxf.Entities.Arc arc) entityPoints = _cadService.ConvertArcToPoints(arc, arcResolutionDegrees);
                else if (dxfEntity is netDxf.Entities.LwPolyline poly) entityPoints = _cadService.ConvertLwPolylineToPoints(poly, arcResolutionDegrees);
                // TODO: Add support for other entity types if CadService is extended.

                if (entityPoints != null && entityPoints.Count >= 2) // A trajectory needs at least two points.
                {
                    // Qualified System.Windows.Shapes.Polyline
                    System.Windows.Shapes.Polyline trajectoryPolyline = new System.Windows.Shapes.Polyline {
                        Points = new PointCollection(entityPoints), // PointCollection takes IEnumerable<System.Windows.Point>
                        Stroke = Brushes.Red, // Trajectory color
                        StrokeThickness = SelectedStrokeThickness, // Make trajectory distinct
                        StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // Dashed line style
                        Tag = TrajectoryPreviewTag // Optional tag for identification
                    };
                    CadCanvas.Children.Add(trajectoryPolyline);
                    _trajectoryPreviewPolylines.Add(trajectoryPolyline); // Keep track for later removal.
                }
            }
            // Update the main status bar with selection count and current configuration/Modbus status.
            string modbusUiStatus = _modbusService.IsConnected ? ModbusStatusTextBlock.Text : "Disconnected";
            StatusTextBlock.Text = $"{_selectedDxfEntities.Count} entities selected. Config: {(_currentConfiguration?.ProductName ?? "Unsaved")}. Modbus: {modbusUiStatus}";
        }

        /// <summary>
        /// Creates a <see cref="Models.Configuration"/> object based on the current UI state and selected entities.
        /// Performs validation on Product Name and Nozzle Number.
        /// </summary>
        /// <param name="forSaving">If true, Product Name validation is strict. If false (e.g. for SendToRobot), a default product name might be used if empty.</param>
        /// <returns>A <see cref="Models.Configuration"/> object, or null if validation fails.</returns>
        private Models.Configuration CreateConfigurationFromCurrentState(bool forSaving = false)
        {
            string productName = ProductNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(productName) && forSaving) {
                MessageBox.Show("Product Name cannot be empty when saving a configuration.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProductNameTextBox.Focus(); return null; }
            else if (string.IsNullOrEmpty(productName) && !forSaving) {
                productName = "DefaultProduct"; // Use a default if not saving but sending.
            }

            if (!int.TryParse(NozzleNumberTextBox.Text, out int nozzleNumber) || nozzleNumber <= 0) {
                MessageBox.Show("Nozzle Number must be a positive integer.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                NozzleNumberTextBox.Focus(); return null; }

            bool isWater = IsWaterCheckBox.IsChecked == true;
            var config = new Models.Configuration { ProductName = productName };
            double arcResolutionDegrees = 15.0; // Standard resolution for trajectory points from arcs/bulges.

            foreach (var dxfEntityObj in _selectedDxfEntities) {
                if (dxfEntityObj is EntityObject dxfEntity) { // Ensure it's a base DXF entity type.
                    var trajectory = new Models.Trajectory {
                        OriginalEntityHandle = dxfEntity.Handle,
                        EntityType = dxfEntity.GetType().Name,
                        NozzleNumber = nozzleNumber,
                        IsWater = isWater
                    };
                    // Convert entity to points based on its type.
                    if (dxfEntity is netDxf.Entities.Line line) trajectory.Points = _cadService.ConvertLineToPoints(line);
                    else if (dxfEntity is netDxf.Entities.Arc arc) trajectory.Points = _cadService.ConvertArcToPoints(arc, arcResolutionDegrees);
                    else if (dxfEntity is netDxf.Entities.LwPolyline poly) trajectory.Points = _cadService.ConvertLwPolylineToPoints(poly, arcResolutionDegrees);
                    // TODO: Add support for other entity types if CadService is extended.
                    config.Trajectories.Add(trajectory); }
            }
            // Display a warning if creating a configuration for sending to robot and trajectory count exceeds robot's limit.
            if (!forSaving && config.Trajectories.Count > 5) {
                MessageBox.Show($"Warning: {config.Trajectories.Count} trajectories defined, but only the first 5 will be sent to the robot.",
                                "Trajectory Limit for Sending", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return config;
        }

        /// <summary>
        /// Handles the Click event of the "Save Config" button.
        /// Creates a configuration from the current state, prompts the user for a save location,
        /// and saves the configuration using <see cref="ConfigurationService"/>.
        /// </summary>
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            string productName = ProductNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(productName)){
                 MessageBox.Show("Product Name cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                 ProductNameTextBox.Focus(); return;
            }
            if (_currentDxfDocument == null || !_selectedDxfEntities.Any()) {
                MessageBox.Show("Load a DXF file and select at least one entity before saving.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Models.Configuration configToSave = CreateConfigurationFromCurrentState(true);
            if (configToSave == null) return;

            if (configToSave.Trajectories.Count > 5) {
                 MessageBoxResult result = MessageBox.Show($"You have selected {configToSave.Trajectories.Count} trajectories. The robot processes a maximum of 5. Do you want to save all selected trajectories in this configuration file for future reference?",
                                                            "Confirm Save Trajectories", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }
            _currentConfiguration = configToSave;

            SaveFileDialog saveFileDialog = new SaveFileDialog {
                Filter = "JSON configuration files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Save RobTeach Configuration",
                FileName = $"{_currentConfiguration.ProductName}.json"
            };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            saveFileDialog.InitialDirectory = initialDir;

            try {
                if (saveFileDialog.ShowDialog() == true) {
                    _configService.SaveConfiguration(_currentConfiguration, saveFileDialog.FileName);
                    _currentLoadedConfigPath = saveFileDialog.FileName;
                    StatusTextBlock.Text = $"Configuration '{_currentConfiguration.ProductName}' saved to {Path.GetFileName(saveFileDialog.FileName)}";
                }
            } catch (Exception ex) { HandleError(ex, "saving configuration"); }
        }

        /// <summary>
        /// Handles the Click event of the "Load Config" button.
        /// Prompts the user to select a configuration file, loads it using <see cref="ConfigurationService"/>,
        /// and applies the loaded configuration to the UI and current DXF (if loaded).
        /// </summary>
        private void LoadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "JSON configuration files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Load RobTeach Configuration"
            };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            openFileDialog.InitialDirectory = initialDir;

            try {
                if (openFileDialog.ShowDialog() == true) {
                    Models.Configuration loadedConfig = _configService.LoadConfiguration(openFileDialog.FileName);
                    if (loadedConfig != null) {
                        _currentConfiguration = loadedConfig;
                        _currentLoadedConfigPath = openFileDialog.FileName;
                        ProductNameTextBox.Text = _currentConfiguration.ProductName;

                        _selectedDxfEntities.Clear();
                        _wpfShapeToDxfEntityMap.Keys.ToList().ForEach(s => { s.Stroke = DefaultStrokeBrush; s.StrokeThickness = DefaultStrokeThickness; });

                        if (_currentDxfDocument == null) {
                            StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded. Please load the corresponding DXF file to see trajectories.";
                            MessageBox.Show("Configuration loaded. Please load the DXF file this configuration applies to.", "DXF Needed", MessageBoxButton.OK, MessageBoxImage.Information);
                            UpdateTrajectoryPreview();
                            return;
                        }

                        if (_currentConfiguration.Trajectories.Any()) {
                            var firstTraj = _currentConfiguration.Trajectories.First();
                            NozzleNumberTextBox.Text = firstTraj.NozzleNumber.ToString();
                            IsWaterCheckBox.IsChecked = firstTraj.IsWater;

                            foreach (var loadedTraj in _currentConfiguration.Trajectories) {
                                if (_dxfEntityHandleMap.TryGetValue(loadedTraj.OriginalEntityHandle, out EntityObject dxfEntity)) {
                                    _selectedDxfEntities.Add(dxfEntity);
                                    var wpfShapeEntry = _wpfShapeToDxfEntityMap.FirstOrDefault(kvp => kvp.Value == dxfEntity);
                                    if (wpfShapeEntry.Key != null) {
                                        wpfShapeEntry.Key.Stroke = SelectedStrokeBrush;
                                        wpfShapeEntry.Key.StrokeThickness = SelectedStrokeThickness;
                                    }
                                } else {
                                    System.Diagnostics.Debug.WriteLine($"Entity with handle {loadedTraj.OriginalEntityHandle} not found in current DXF.");
                                }
                            }
                            StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded. {_selectedDxfEntities.Count} entities re-selected.";
                        } else { StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded, but it contains no trajectories."; }
                        UpdateTrajectoryPreview();
                    } else {
                        StatusTextBlock.Text = "Failed to load or parse configuration file.";
                        MessageBox.Show("The selected file could not be loaded as a valid configuration.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            } catch (Exception ex) { HandleError(ex, "loading configuration"); }
        }

        /// <summary>
        /// Handles the Click event of the "Connect" button for Modbus communication.
        /// Validates IP and Port, then attempts to connect using <see cref="ModbusService"/>.
        /// Updates UI elements to reflect connection status.
        /// </summary>
        private void ModbusConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string ipAddress = ModbusIpAddressTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ipAddress)) {
                MessageBox.Show("Modbus IP Address cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusTextBlock.Text = "Modbus IP Address required."; ModbusIpAddressTextBox.Focus(); return;
            }
            if (ipAddress.Contains(" ") || ipAddress.Contains(",")) {
                MessageBox.Show("Modbus IP Address contains invalid characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusTextBlock.Text = "Invalid Modbus IP Address format."; ModbusIpAddressTextBox.Focus(); return;
            }

            if (!int.TryParse(ModbusPortTextBox.Text, out int port) || port < 1 || port > 65535) {
                MessageBox.Show("Modbus Port must be a valid integer between 1 and 65535.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusTextBlock.Text = "Invalid Modbus Port."; ModbusPortTextBox.Focus(); return; }

            StatusTextBlock.Text = $"Connecting to Modbus server at {ipAddress}:{port}...";
            ModbusStatusTextBlock.Text = "Connecting...";
            ModbusConnectButton.IsEnabled = false;
            ModbusDisconnectButton.IsEnabled = false;

            ModbusResponse response = _modbusService.Connect(ipAddress, port);

            StatusTextBlock.Text = response.Message;
            ModbusStatusTextBlock.Text = response.Success ? "Connected" : "Failed";

            if (response.Success) {
                ModbusStatusIndicatorEllipse.Fill = Brushes.LimeGreen;
                ModbusDisconnectButton.IsEnabled = true;
            } else {
                ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
                ModbusConnectButton.IsEnabled = true;
                MessageBox.Show(response.Message, "Modbus Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles the Click event of the "Disconnect" button for Modbus communication.
        /// Disconnects using <see cref="ModbusService"/> and updates UI elements.
        /// </summary>
        private void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _modbusService.Disconnect();
            ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
            ModbusStatusTextBlock.Text = "Disconnected";
            ModbusConnectButton.IsEnabled = true;
            ModbusDisconnectButton.IsEnabled = false;
            StatusTextBlock.Text = "Modbus disconnected.";
        }

        /// <summary>
        /// Handles the Click event of the "Send to Robot" button.
        /// Validates Modbus connection and trajectory data, then sends the configuration using <see cref="ModbusService"/>.
        /// </summary>
        private void SendToRobotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) {
                MessageBox.Show("Not connected. Please connect to Modbus server first.", "Modbus Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Models.Configuration configToSend = _currentConfiguration;
            if (configToSend == null || !configToSend.Trajectories.Any()) {
                if (_selectedDxfEntities.Any()) {
                    configToSend = CreateConfigurationFromCurrentState(false);
                }
            }

            if (configToSend == null || !configToSend.Trajectories.Any()) {
                MessageBox.Show("No trajectories available to send. Please select entities on a loaded DXF or load/save a valid configuration.", "Send to Robot", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            SendToRobotButton.IsEnabled = false;
            StatusTextBlock.Text = $"Sending {configToSend.Trajectories.Count} trajectories for '{configToSend.ProductName}' to robot...";

            ModbusResponse response = _modbusService.SendConfiguration(configToSend);

            StatusTextBlock.Text = response.Message;
            SendToRobotButton.IsEnabled = true;

            MessageBox.Show(response.Message, "Modbus Send Status", MessageBoxButton.OK,
                            response.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        // --- Canvas Zoom/Pan/FitToView Methods ---

        /// <summary>
        /// Calculates the bounding box of all entities in the provided DXF document.
        /// Uses netDxf's BoundingBox property for entities and drawing extents.
        /// </summary>
        /// <param name="dxfDoc">The <see cref="DxfDocument"/> to analyze.</param>
        /// <returns>A <see cref="Rect"/> representing the overall bounding box in DXF coordinates.</returns>
        private Rect GetDxfBoundingBox(DxfDocument dxfDoc) {
            if (dxfDoc == null) return Rect.Empty;
            var extMin = dxfDoc.DrawingVariables.ExtMin;
            var extMax = dxfDoc.DrawingVariables.ExtMax;
            var docSize = extMax - extMin;

            if (docSize.Length > 0.0001) {
                 return new Rect(extMin.X, extMin.Y, docSize.X, docSize.Y);
            }
            BoundingRectangle overallBox = null;
            if (dxfDoc.Entities.All.Any()) {
                foreach (var entity in dxfDoc.Entities.All) {
                    var entityBox = entity.BoundingBox;
                    if (entityBox != null) {
                        if (overallBox == null) overallBox = new BoundingRectangle(entityBox.Min, entityBox.Max);
                        else overallBox.Union(entityBox);
                    }
                }
            }
            if (overallBox == null) return Rect.Empty;
            return new Rect(overallBox.Min.X, overallBox.Min.Y, overallBox.Width, overallBox.Height);
        }

        /// <summary>
        /// Handles the Click event of the "Fit to View" button.
        /// Adjusts the canvas zoom and pan to display the entire loaded DXF drawing.
        /// </summary>
        private void FitToViewButton_Click(object sender, RoutedEventArgs e) {
            if (_currentDxfDocument == null) { StatusTextBlock.Text = "Load a DXF file first to use Fit to View."; return; }
            if(_dxfBoundingBox.IsEmpty) _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
            PerformFitToView();
        }

        /// <summary>
        /// Adjusts the canvas's scale and translation transforms to fit the stored <see cref="_dxfBoundingBox"/>
        /// within the visible area of the <see cref="CadCanvas"/>. Includes Y-axis inversion for DXF.
        /// </summary>
        private void PerformFitToView() {
            if (_dxfBoundingBox.IsEmpty || CadCanvas.ActualWidth == 0 || CadCanvas.ActualHeight == 0) {
                _scaleTransform.ScaleX = 1;
                _scaleTransform.ScaleY = -1;
                _translateTransform.X = 0;
                _translateTransform.Y = CadCanvas.ActualHeight;
                return;
            }
            double margin = 20;
            double canvasWidth = Math.Max(1, CadCanvas.ActualWidth - 2 * margin);
            double canvasHeight = Math.Max(1, CadCanvas.ActualHeight - 2 * margin);
            double dxfWidth = Math.Max(1, _dxfBoundingBox.Width);
            double dxfHeight = Math.Max(1, _dxfBoundingBox.Height);

            double scaleX = canvasWidth / dxfWidth;
            double scaleY = canvasHeight / dxfHeight;
            double newScale = Math.Min(scaleX, scaleY);

            _scaleTransform.ScaleX = newScale;
            _scaleTransform.ScaleY = -newScale;

            double dxfCenterX = _dxfBoundingBox.Left + dxfWidth / 2.0;
            double dxfCenterY = _dxfBoundingBox.Top + dxfHeight / 2.0;

            _translateTransform.X = -dxfCenterX * _scaleTransform.ScaleX + (CadCanvas.ActualWidth / 2.0);
            _translateTransform.Y = -dxfCenterY * _scaleTransform.ScaleY + (CadCanvas.ActualHeight / 2.0);

            StatusTextBlock.Text = "View fitted to content.";
        }

        /// <summary>
        /// Handles the MouseWheel event on the <see cref="CadCanvas"/> for zooming.
        /// Zooms in or out relative to the mouse cursor's position.
        /// </summary>
        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
            if (_currentDxfDocument == null) return;
            System.Windows.Point position = e.GetPosition(CadCanvas); // Qualified System.Windows.Point
            double zoomFactor = e.Delta > 0 ? 1.15 : 1 / 1.15;

            double oldScaleX = _scaleTransform.ScaleX;
            double oldScaleY = _scaleTransform.ScaleY;

            _scaleTransform.ScaleX *= zoomFactor;
            _scaleTransform.ScaleY *= zoomFactor;

            _translateTransform.X = position.X - (position.X - _translateTransform.X) * (_scaleTransform.ScaleX / oldScaleX);
            _translateTransform.Y = position.Y - (position.Y - _translateTransform.Y) * (_scaleTransform.ScaleY / oldScaleY);
            e.Handled = true;
        }

        /// <summary>
        /// Handles the MouseDown event on the <see cref="CadCanvas"/> to initiate panning.
        /// </summary>
        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e) {
            if (_currentDxfDocument == null) return;
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed) {
                _isPanning = true;
                _panStartPoint = e.GetPosition(CadCanvas); // Returns System.Windows.Point
                CadCanvas.CaptureMouse();
                CadCanvas.Cursor = Cursors.ScrollAll;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles the MouseMove event on the <see cref="CadCanvas"/> for active panning.
        /// </summary>
        private void CadCanvas_MouseMove(object sender, MouseEventArgs e) {
            if (_isPanning && e.MiddleButton == MouseButtonState.Pressed) {
                System.Windows.Point currentPoint = e.GetPosition(CadCanvas); // Qualified System.Windows.Point
                Vector delta = currentPoint - _panStartPoint;
                _translateTransform.X += delta.X;
                _translateTransform.Y += delta.Y;
                _panStartPoint = currentPoint;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles the MouseUp event on the <see cref="CadCanvas"/> to end panning.
        /// </summary>
        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Middle && _isPanning) {
                _isPanning = false;
                CadCanvas.ReleaseMouseCapture();
                CadCanvas.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Generic error handler to display messages in StatusBar and a MessageBox.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="action">A string describing the action being performed when the error occurred (e.g., "loading DXF").</param>
        private void HandleError(Exception ex, string action) {
            StatusTextBlock.Text = $"Error {action}: {ex.Message}";
            MessageBox.Show($"An error occurred while {action}:\n{ex.ToString()}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
