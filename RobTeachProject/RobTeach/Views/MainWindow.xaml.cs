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
        private const double TrajectoryPointResolutionAngle = 15.0; // Default resolution for discretizing arcs/circles.


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
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            openFileDialog.InitialDirectory = initialDir;

            try {
                if (openFileDialog.ShowDialog() == true) {
                    _currentDxfFilePath = openFileDialog.FileName;
                    StatusTextBlock.Text = $"Loading DXF: {Path.GetFileName(_currentDxfFilePath)}...";

                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear(); _selectedDxfEntities.Clear();
                    _trajectoryPreviewPolylines.Clear(); _dxfEntityHandleMap.Clear();
                    _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                    _currentLoadedConfigPath = null;
                    _currentDxfDocument = null;
                    _dxfBoundingBox = Rect.Empty;
                    UpdateTrajectoryPreview();

                    _currentDxfDocument = _cadService.LoadDxf(_currentDxfFilePath);

                    if (_currentDxfDocument == null) {
                        StatusTextBlock.Text = "Failed to load DXF document (null document returned).";
                        MessageBox.Show("The DXF document could not be loaded. The file might be empty or an unknown error occurred.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    foreach(var entity in _currentDxfDocument.Entities.All) { // For handle map
                        if (!string.IsNullOrEmpty(entity.Handle)) _dxfEntityHandleMap[entity.Handle] = entity; }

                    List<System.Windows.Shapes.Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument);
                    int shapeIndex = 0;
                    Action<EntityObject> mapAndAddShape = (dxfEntity) => {
                        // This relies on GetWpfShapesFromDxf returning shapes in a specific order (Lines, Arcs, Circles)
                        // and that the counts match the iteration here.
                        if (shapeIndex < wpfShapes.Count && wpfShapes[shapeIndex] != null) {
                            var wpfShape = wpfShapes[shapeIndex];
                            wpfShape.Stroke = DefaultStrokeBrush; wpfShape.StrokeThickness = DefaultStrokeThickness;
                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked;
                            _wpfShapeToDxfEntityMap[wpfShape] = dxfEntity;
                            CadCanvas.Children.Add(wpfShape);
                            shapeIndex++; }};

                    _currentDxfDocument.Entities.Lines.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.Entities.Arcs.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.Entities.Circles.ToList().ForEach(mapAndAddShape); // Added Circles
                    // LwPolyline iteration removed: _currentDxfDocument.Entities.LwPolylines.ToList().ForEach(mapAndAddShape);

                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                    PerformFitToView();
                    StatusTextBlock.Text = $"Loaded: {Path.GetFileName(_currentDxfFilePath)}. Click shapes to select.";
                } else { StatusTextBlock.Text = "DXF loading cancelled."; }
            }
            catch (FileNotFoundException fnfEx) {
                StatusTextBlock.Text = "Error: DXF file not found.";
                MessageBox.Show($"DXF file not found:\n{fnfEx.Message}", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            catch (netDxf.DxfVersionNotSupportedException dxfVerEx) {
                StatusTextBlock.Text = "Error: DXF version not supported.";
                MessageBox.Show($"The DXF file version is not supported by the parser:\n{dxfVerEx.Message}", "DXF Version Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            catch (Exception ex) {
                StatusTextBlock.Text = "Error loading or processing DXF file.";
                MessageBox.Show($"An error occurred while loading or processing the DXF file:\n{ex.Message}\n\nEnsure the file is a valid DXF format.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
                CadCanvas.Children.Clear();
                _selectedDxfEntities.Clear(); _wpfShapeToDxfEntityMap.Clear(); _dxfEntityHandleMap.Clear();
                _trajectoryPreviewPolylines?.Clear();
                _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                UpdateTrajectoryPreview();
            }
        }

        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }

        private void UpdateTrajectoryPreview()
        {
            _trajectoryPreviewPolylines.ForEach(p => CadCanvas.Children.Remove(p));
            _trajectoryPreviewPolylines.Clear();

            // Generate and draw trajectories for each selected DXF entity.
            foreach (object dxfEntity in _selectedDxfEntities)
            {
                List<System.Windows.Point> entityPoints = null;
                if (dxfEntity is netDxf.Entities.Line line) entityPoints = _cadService.ConvertLineToPoints(line);
                else if (dxfEntity is netDxf.Entities.Arc arc) entityPoints = _cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle);
                else if (dxfEntity is netDxf.Entities.Circle circ) entityPoints = _cadService.ConvertCircleToPoints(circ, TrajectoryPointResolutionAngle); // Added Circle case
                // else if (dxfEntity is LightWeightPolyline poly) // LwPolyline case removed
                // {
                //     // entityPoints = _cadService.ConvertLwPolylineToPoints(poly, TrajectoryPointResolutionAngle);
                // }

                if (entityPoints != null && entityPoints.Count >= 2)
                {
                    System.Windows.Shapes.Polyline trajectoryPolyline = new System.Windows.Shapes.Polyline {
                        Points = new PointCollection(entityPoints),
                        Stroke = Brushes.Red,
                        StrokeThickness = SelectedStrokeThickness,
                        StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }),
                        Tag = TrajectoryPreviewTag
                    };
                    CadCanvas.Children.Add(trajectoryPolyline);
                    _trajectoryPreviewPolylines.Add(trajectoryPolyline);
                }
            }
            string modbusUiStatus = _modbusService.IsConnected ? ModbusStatusTextBlock.Text : "Disconnected";
            StatusTextBlock.Text = $"{_selectedDxfEntities.Count} entities selected. Config: {(_currentConfiguration?.ProductName ?? "Unsaved")}. Modbus: {modbusUiStatus}";
        }

        private Models.Configuration CreateConfigurationFromCurrentState(bool forSaving = false)
        {
            string productName = ProductNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(productName) && forSaving) {
                MessageBox.Show("Product Name cannot be empty when saving a configuration.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProductNameTextBox.Focus(); return null; }
            else if (string.IsNullOrEmpty(productName) && !forSaving) {
                productName = "DefaultProduct";
            }

            if (!int.TryParse(NozzleNumberTextBox.Text, out int nozzleNumber) || nozzleNumber <= 0) {
                MessageBox.Show("Nozzle Number must be a positive integer.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                NozzleNumberTextBox.Focus(); return null; }

            bool isWater = IsWaterCheckBox.IsChecked == true;
            var config = new Models.Configuration { ProductName = productName };

            foreach (var dxfEntityObj in _selectedDxfEntities) {
                if (dxfEntityObj is EntityObject dxfEntity) {
                    var trajectory = new Models.Trajectory {
                        OriginalEntityHandle = dxfEntity.Handle,
                        EntityType = dxfEntity.GetType().Name,
                        NozzleNumber = nozzleNumber,
                        IsWater = isWater
                    };
                    if (dxfEntity is netDxf.Entities.Line line) trajectory.Points = _cadService.ConvertLineToPoints(line);
                    else if (dxfEntity is netDxf.Entities.Arc arc) trajectory.Points = _cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle);
                    else if (dxfEntity is netDxf.Entities.Circle circ) trajectory.Points = _cadService.ConvertCircleToPoints(circ, TrajectoryPointResolutionAngle); // Added Circle case
                    // else if (dxfEntity is LightWeightPolyline poly) // LwPolyline case removed
                    // {
                    //    // trajectory.Points = _cadService.ConvertLwPolylineToPoints(poly, TrajectoryPointResolutionAngle);
                    // }
                    if(trajectory.Points != null && trajectory.Points.Any()) // Only add if points were generated
                    {
                        config.Trajectories.Add(trajectory);
                    }
                }
            }
            if (!forSaving && config.Trajectories.Count > 5) {
                MessageBox.Show($"Warning: {config.Trajectories.Count} trajectories defined, but only the first 5 will be sent to the robot.",
                                "Trajectory Limit for Sending", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return config;
        }

        private void SaveConfigButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void LoadConfigButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void ModbusConnectButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void SendToRobotButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private Rect GetDxfBoundingBox(DxfDocument dxfDoc) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void FitToViewButton_Click(object sender, RoutedEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void PerformFitToView() { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void CadCanvas_MouseMove(object sender, MouseEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e) { /* ... (No change needed here for LwPolyline removal) ... */ }
        private void HandleError(Exception ex, string action) { /* ... (No change needed here for LwPolyline removal) ... */ }
    }
}
