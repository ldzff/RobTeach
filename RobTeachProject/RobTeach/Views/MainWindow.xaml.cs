using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
using System.Windows.Threading; // For Dispatcher to force UI update (optional)

namespace RobTeach.Views
{
    public partial class MainWindow : Window
    {
        private readonly CadService _cadService = new CadService();
        private readonly ConfigurationService _configService = new ConfigurationService();
        private readonly ModbusService _modbusService = new ModbusService();

        private DxfDocument _currentDxfDocument;
        private string _currentDxfFilePath;
        private string _currentLoadedConfigPath;
        private Models.Configuration _currentConfiguration;

        private readonly List<object> _selectedDxfEntities = new List<object>();
        private readonly Dictionary<Shape, object> _wpfShapeToDxfEntityMap = new Dictionary<Shape, object>();
        private readonly Dictionary<string, EntityObject> _dxfEntityHandleMap = new Dictionary<string, EntityObject>();
        private readonly List<Polyline> _trajectoryPreviewPolylines = new List<Polyline>();

        private ScaleTransform _scaleTransform;
        private TranslateTransform _translateTransform;
        private TransformGroup _transformGroup;
        private Point _panStartPoint;
        private bool _isPanning;
        private Rect _dxfBoundingBox = Rect.Empty;

        private static readonly Brush DefaultStrokeBrush = Brushes.DarkSlateGray;
        private static readonly Brush SelectedStrokeBrush = Brushes.DodgerBlue;
        private const double DefaultStrokeThickness = 1;
        private const double SelectedStrokeThickness = 2.5;
        private const string TrajectoryPreviewTag = "TrajectoryPreview";

        public MainWindow()
        {
            InitializeComponent();
            if (CadCanvas.Background == null) CadCanvas.Background = Brushes.LightGray;
            ProductNameTextBox.Text = $"Product_{DateTime.Now:yyyyMMddHHmmss}";
            _currentConfiguration = new Models.Configuration();
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            _scaleTransform = new ScaleTransform(1, 1);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            CadCanvas.RenderTransform = _transformGroup;

            CadCanvas.MouseWheel += CadCanvas_MouseWheel;
            CadCanvas.MouseDown += CadCanvas_MouseDown;
            CadCanvas.MouseMove += CadCanvas_MouseMove;
            CadCanvas.MouseUp += CadCanvas_MouseUp;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _modbusService.Disconnect();
        }

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
                    _currentDxfDocument = _cadService.LoadDxf(_currentDxfFilePath);

                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear(); _selectedDxfEntities.Clear();
                    _trajectoryPreviewPolylines.Clear(); _dxfEntityHandleMap.Clear();
                    _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                    _currentLoadedConfigPath = null;

                    if (_currentDxfDocument == null) { StatusTextBlock.Text = "Failed to load DXF document."; return; }

                    foreach(var entity in _currentDxfDocument.Entities.All) {
                        if (!string.IsNullOrEmpty(entity.Handle)) _dxfEntityHandleMap[entity.Handle] = entity; }

                    List<Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument);
                    int shapeIndex = 0;
                    Action<EntityObject> mapAndAddShape = (dxfEntity) => {
                        if (shapeIndex < wpfShapes.Count && wpfShapes[shapeIndex] != null) {
                            var wpfShape = wpfShapes[shapeIndex];
                            wpfShape.Stroke = DefaultStrokeBrush; wpfShape.StrokeThickness = DefaultStrokeThickness;
                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked;
                            _wpfShapeToDxfEntityMap[wpfShape] = dxfEntity; CadCanvas.Children.Add(wpfShape); shapeIndex++; }};
                    _currentDxfDocument.Lines.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.Arcs.ToList().ForEach(mapAndAddShape);
                    _currentDxfDocument.LwPolylines.ToList().ForEach(mapAndAddShape);

                    UpdateTrajectoryPreview();
                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                    PerformFitToView();
                    StatusTextBlock.Text = $"Loaded: {Path.GetFileName(_currentDxfFilePath)}. Click shapes to select.";
                } else { StatusTextBlock.Text = "DXF loading cancelled."; }
            } catch (Exception ex) { HandleError(ex, "loading DXF"); }
        }

        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            if (sender is Shape clickedShape && _wpfShapeToDxfEntityMap.TryGetValue(clickedShape, out object dxfEntity)) {
                if (_selectedDxfEntities.Contains(dxfEntity)) {
                    _selectedDxfEntities.Remove(dxfEntity);
                    clickedShape.Stroke = DefaultStrokeBrush; clickedShape.StrokeThickness = DefaultStrokeThickness;
                } else {
                    _selectedDxfEntities.Add(dxfEntity);
                    clickedShape.Stroke = SelectedStrokeBrush; clickedShape.StrokeThickness = SelectedStrokeThickness;
                }
                UpdateTrajectoryPreview();
                e.Handled = true;
            }
        }

        private void UpdateTrajectoryPreview()
        {
            _trajectoryPreviewPolylines.ForEach(p => CadCanvas.Children.Remove(p));
            _trajectoryPreviewPolylines.Clear();
            double arcRes = 15.0;
            foreach (object dxfEntity in _selectedDxfEntities) {
                List<Point> entityPoints = null;
                if (dxfEntity is netDxf.Entities.Line line) entityPoints = _cadService.ConvertLineToPoints(line);
                else if (dxfEntity is netDxf.Entities.Arc arc) entityPoints = _cadService.ConvertArcToPoints(arc, arcRes);
                else if (dxfEntity is netDxf.Entities.LwPolyline poly) entityPoints = _cadService.ConvertLwPolylineToPoints(poly, arcRes);
                if (entityPoints != null && entityPoints.Count >= 2) {
                    Polyline trajPoly = new Polyline { Points = new PointCollection(entityPoints), Stroke = Brushes.Red,
                        StrokeThickness = SelectedStrokeThickness, StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), Tag = TrajectoryPreviewTag };
                    CadCanvas.Children.Add(trajPoly); _trajectoryPreviewPolylines.Add(trajPoly); }
            }
            string modbusUiStatus = _modbusService.IsConnected ? ModbusStatusTextBlock.Text : "Disconnected"; // Use the specific Modbus status text
            StatusTextBlock.Text = $"{_selectedDxfEntities.Count} entities selected. Config: {(_currentConfiguration?.ProductName ?? "Unsaved")}. Modbus: {modbusUiStatus}";
        }

        private Models.Configuration CreateConfigurationFromCurrentState(bool forSaving = false) {
            if (!int.TryParse(NozzleNumberTextBox.Text, out int nozzleNumber) || nozzleNumber <= 0) {
                MessageBox.Show("Valid nozzle number required.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            bool isWater = IsWaterCheckBox.IsChecked == true;
            string productName = ProductNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(productName)) {
                MessageBox.Show("Product name required.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning); return null; }
            var config = new Models.Configuration { ProductName = productName };
            double arcRes = 15.0;
            foreach (var dxfEntityObj in _selectedDxfEntities) {
                if (dxfEntityObj is EntityObject dxfEntity) {
                    var trajectory = new Models.Trajectory { OriginalEntityHandle = dxfEntity.Handle, EntityType = dxfEntity.GetType().Name, NozzleNumber = nozzleNumber, IsWater = isWater };
                    if (dxfEntity is netDxf.Entities.Line line) trajectory.Points = _cadService.ConvertLineToPoints(line);
                    else if (dxfEntity is netDxf.Entities.Arc arc) trajectory.Points = _cadService.ConvertArcToPoints(arc, arcRes);
                    else if (dxfEntity is netDxf.Entities.LwPolyline poly) trajectory.Points = _cadService.ConvertLwPolylineToPoints(poly, arcRes);
                    config.Trajectories.Add(trajectory); }
            }
            if (!forSaving && config.Trajectories.Count > 5) { // This warning is for "SendToRobot" context
                MessageBox.Show($"Warning: {config.Trajectories.Count} trajectories defined, but only the first 5 will be sent to the robot.",
                                "Trajectory Limit for Sending", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return config;
        }
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e) {
            if (_currentDxfDocument == null || !_selectedDxfEntities.Any()) {
                MessageBox.Show("Load DXF & select entities first.", "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            Models.Configuration configToSave = CreateConfigurationFromCurrentState(true);
            if (configToSave == null) return;

            if (configToSave.Trajectories.Count > 5) // This warning is specific to saving more than 5
            {
                 MessageBoxResult result = MessageBox.Show($"You have selected {configToSave.Trajectories.Count} trajectories. While the robot processes a maximum of 5, do you want to save all selected trajectories in this configuration file for future reference?",
                                                            "Confirm Save Trajectories", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return; // User chose not to save.
            }
            _currentConfiguration = configToSave;

            SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json|All (*.*)|*.*", Title = "Save Configuration", FileName = $"{_currentConfiguration.ProductName}.json" };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            saveFileDialog.InitialDirectory = initialDir;
            try { if (saveFileDialog.ShowDialog() == true) {
                    _configService.SaveConfiguration(_currentConfiguration, saveFileDialog.FileName);
                    _currentLoadedConfigPath = saveFileDialog.FileName;
                    StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' saved to {Path.GetFileName(saveFileDialog.FileName)}";
            }} catch (Exception ex) { HandleError(ex, "saving configuration"); }
        }
        private void LoadConfigButton_Click(object sender, RoutedEventArgs e) {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All (*.*)|*.*", Title = "Load Configuration" };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            openFileDialog.InitialDirectory = initialDir;
            try { if (openFileDialog.ShowDialog() == true) {
                    Models.Configuration loadedConfig = _configService.LoadConfiguration(openFileDialog.FileName);
                    if (loadedConfig != null) {
                        _currentConfiguration = loadedConfig; _currentLoadedConfigPath = openFileDialog.FileName;
                        ProductNameTextBox.Text = _currentConfiguration.ProductName;
                        _selectedDxfEntities.Clear();
                        _wpfShapeToDxfEntityMap.Keys.ToList().ForEach(s => { s.Stroke = DefaultStrokeBrush; s.StrokeThickness = DefaultStrokeThickness; });
                        if (_currentDxfDocument == null) {
                            StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded. Load DXF to see trajectories.";
                            MessageBox.Show("Config loaded. Load relevant DXF.", "DXF Needed", MessageBoxButton.OK, MessageBoxImage.Information); UpdateTrajectoryPreview(); return; }
                        if (_currentConfiguration.Trajectories.Any()) {
                            var firstTraj = _currentConfiguration.Trajectories.First();
                            NozzleNumberTextBox.Text = firstTraj.NozzleNumber.ToString(); IsWaterCheckBox.IsChecked = firstTraj.IsWater;
                            foreach (var loadedTraj in _currentConfiguration.Trajectories) {
                                if (_dxfEntityHandleMap.TryGetValue(loadedTraj.OriginalEntityHandle, out EntityObject dxfEntity)) {
                                    _selectedDxfEntities.Add(dxfEntity);
                                    var wpfShapeEntry = _wpfShapeToDxfEntityMap.FirstOrDefault(kvp => kvp.Value == dxfEntity);
                                    if (wpfShapeEntry.Key != null) { wpfShapeEntry.Key.Stroke = SelectedStrokeBrush; wpfShapeEntry.Key.StrokeThickness = SelectedStrokeThickness; } } }
                            StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded. {_selectedDxfEntities.Count} entities re-selected.";
                        } else { StatusTextBlock.Text = $"Config '{_currentConfiguration.ProductName}' loaded (0 trajectories)."; }
                        UpdateTrajectoryPreview();
                    } else { StatusTextBlock.Text = "Failed to load config."; MessageBox.Show("Could not load config.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error); } }
            } catch (Exception ex) { HandleError(ex, "loading configuration"); }
        }

        private void ModbusConnectButton_Click(object sender, RoutedEventArgs e)
        {
            string ip = ModbusIpAddressTextBox.Text;
            if (!int.TryParse(ModbusPortTextBox.Text, out int port) || port <= 0 || port > 65535) {
                MessageBox.Show("Invalid Modbus port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            StatusTextBlock.Text = $"Connecting to {ip}:{port}...";
            ModbusStatusTextBlock.Text = "Connecting...";
            ModbusConnectButton.IsEnabled = false;
            ModbusDisconnectButton.IsEnabled = false;

            ModbusResponse response = _modbusService.Connect(ip, port);
            StatusTextBlock.Text = response.Message;
            ModbusStatusTextBlock.Text = response.Success ? "Connected" : "Failed";

            if (response.Success) {
                ModbusStatusIndicatorEllipse.Fill = Brushes.LimeGreen;
                ModbusDisconnectButton.IsEnabled = true;
            } else {
                ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
                ModbusConnectButton.IsEnabled = true; // Allow retry
                MessageBox.Show(response.Message, "Modbus Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _modbusService.Disconnect();
            ModbusStatusIndicatorEllipse.Fill = Brushes.Red;
            ModbusStatusTextBlock.Text = "Disconnected";
            ModbusConnectButton.IsEnabled = true;
            ModbusDisconnectButton.IsEnabled = false;
            StatusTextBlock.Text = "Modbus disconnected.";
        }

        private void SendToRobotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_modbusService.IsConnected) {
                MessageBox.Show("Not connected. Please connect to Modbus server first.", "Modbus Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            Models.Configuration configToSend = _currentConfiguration;
            if (configToSend == null || !configToSend.Trajectories.Any()) {
                if (_selectedDxfEntities.Any()) {
                    configToSend = CreateConfigurationFromCurrentState(false); // forSaving = false (sending context)
                }
            }

            if (configToSend == null || !configToSend.Trajectories.Any()) {
                MessageBox.Show("No trajectories to send. Select entities or load/save a config.", "Send to Robot", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            SendToRobotButton.IsEnabled = false;
            StatusTextBlock.Text = "Sending data to robot...";
            // Optional: Force UI update if needed, though MessageBox might suffice
            // Dispatcher.Invoke(() => {}, DispatcherPriority.Background);

            ModbusResponse response = _modbusService.SendConfiguration(configToSend);

            StatusTextBlock.Text = response.Message;
            SendToRobotButton.IsEnabled = true;

            MessageBox.Show(response.Message, "Modbus Send Status", MessageBoxButton.OK,
                            response.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        private Rect GetDxfBoundingBox(DxfDocument dxfDoc) { /* ... (no change) ... */
            if (dxfDoc == null) return Rect.Empty;
            var docBounds = dxfDoc.DrawingVariables.ExtMax - dxfDoc.DrawingVariables.ExtMin;
            if (docBounds.Length <= 0 && dxfDoc.Entities.All.Any()) {
                BoundingRectangle overallBox = null;
                foreach (var entity in dxfDoc.Entities.All) {
                    var entityBox = entity.BoundingBox;
                    if (entityBox != null) {
                        if (overallBox == null) overallBox = entityBox; else overallBox.Union(entityBox); } }
                if (overallBox == null) return Rect.Empty;
                return new Rect(overallBox.Min.X, overallBox.Min.Y, overallBox.Width, overallBox.Height); }
            return new Rect(dxfDoc.DrawingVariables.ExtMin.X, dxfDoc.DrawingVariables.ExtMin.Y, docBounds.X, docBounds.Y);
        }
        private void FitToViewButton_Click(object sender, RoutedEventArgs e) { /* ... (no change) ... */
            if (_currentDxfDocument == null) { StatusTextBlock.Text = "Load a DXF file first to use Fit to View."; return; }
            if(_dxfBoundingBox.IsEmpty) _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument); PerformFitToView();
        }
        private void PerformFitToView() { /* ... (no change) ... */
            if (_dxfBoundingBox.IsEmpty || CadCanvas.ActualWidth == 0 || CadCanvas.ActualHeight == 0) {
                _scaleTransform.ScaleX = 1; _scaleTransform.ScaleY = -1;
                _translateTransform.X = 0; _translateTransform.Y = CadCanvas.ActualHeight; return; }
            double margin = 20;
            double canvasWidth = Math.Max(1, CadCanvas.ActualWidth - 2 * margin);
            double canvasHeight = Math.Max(1, CadCanvas.ActualHeight - 2 * margin);
            double dxfWidth = Math.Max(1, _dxfBoundingBox.Width);
            double dxfHeight = Math.Max(1, _dxfBoundingBox.Height);
            double scaleX = canvasWidth / dxfWidth; double scaleY = canvasHeight / dxfHeight;
            double newScale = Math.Min(scaleX, scaleY);
            _scaleTransform.ScaleX = newScale; _scaleTransform.ScaleY = -newScale;
            double dxfCenterX = _dxfBoundingBox.Left + dxfWidth / 2.0;
            double dxfCenterY = _dxfBoundingBox.Top + dxfHeight / 2.0;
            _translateTransform.X = -dxfCenterX * _scaleTransform.ScaleX + (CadCanvas.ActualWidth / 2.0);
            _translateTransform.Y = -dxfCenterY * _scaleTransform.ScaleY + (CadCanvas.ActualHeight / 2.0);
            StatusTextBlock.Text = "View fitted to content.";
        }
        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e) { /* ... (no change) ... */
            if (_currentDxfDocument == null) return; Point position = e.GetPosition(CadCanvas);
            double zoomFactor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double oldScaleX = _scaleTransform.ScaleX; double oldScaleY = _scaleTransform.ScaleY;
            _scaleTransform.ScaleX *= zoomFactor; _scaleTransform.ScaleY *= zoomFactor;
            _translateTransform.X = position.X - (position.X - _translateTransform.X) * (_scaleTransform.ScaleX / oldScaleX);
            _translateTransform.Y = position.Y - (position.Y - _translateTransform.Y) * (_scaleTransform.ScaleY / oldScaleY);
            e.Handled = true;
        }
        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e) { /* ... (no change) ... */
            if (_currentDxfDocument == null) return;
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed) {
                _isPanning = true; _panStartPoint = e.GetPosition(CadCanvas);
                CadCanvas.CaptureMouse(); CadCanvas.Cursor = Cursors.ScrollAll; e.Handled = true; }
        }
        private void CadCanvas_MouseMove(object sender, MouseEventArgs e) { /* ... (no change) ... */
            if (_isPanning && e.MiddleButton == MouseButtonState.Pressed) {
                Point currentPoint = e.GetPosition(CadCanvas); Vector delta = currentPoint - _panStartPoint;
                _translateTransform.X += delta.X; _translateTransform.Y += delta.Y;
                _panStartPoint = currentPoint;  e.Handled = true; }
        }
        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e) { /* ... (no change) ... */
            if (e.ChangedButton == MouseButton.Middle && _isPanning) {
                _isPanning = false; CadCanvas.ReleaseMouseCapture(); CadCanvas.Cursor = Cursors.Arrow; e.Handled = true; }
        }
        private void HandleError(Exception ex, string action) {
            StatusTextBlock.Text = $"Error {action}: {ex.Message}";
            MessageBox.Show($"An error occurred while {action}:\n{ex.ToString()}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
