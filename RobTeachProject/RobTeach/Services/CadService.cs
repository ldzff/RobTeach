using netDxf;
using netDxf.Entities;
// using netDxf.Tables; // Currently not used directly, consider removing if not needed
// using netDxf.Units; // Currently not used directly, consider removing if not needed
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
// using System.Windows.Shapes; // Can be removed if all usages are qualified

namespace RobTeach.Services
{
    /// <summary>
    /// Provides services for loading CAD (DXF) files and converting DXF entities
    /// into WPF shapes and trajectory points.
    /// </summary>
    public class CadService
    {
        /// <summary>
        /// Loads a DXF document from the specified file path.
        /// </summary>
        /// <param name="filePath">The path to the DXF file.</param>
        /// <returns>A <see cref="DxfDocument"/> object if loading is successful.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="filePath"/> is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown if the DXF file is not found at the specified path.</exception>
        /// <exception cref="Exception">Thrown if an error occurs during DXF parsing (e.g., invalid format, version not supported by netDxf library).</exception>
        public DxfDocument LoadDxf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }
            if (!File.Exists(filePath))
            {
                // This exception is specific and helpful for UI to catch.
                throw new FileNotFoundException("DXF file not found.", filePath);
            }
            try
            {
                // netDxf.DxfDocument.Load can throw various exceptions including DxfVersionNotSupportedException
                // or others if the file is corrupt or not a valid DXF.
                DxfDocument dxf = DxfDocument.Load(filePath);
                return dxf;
            }
            catch (Exception ex) // Catching general Exception as netDxf can throw various internal errors.
            {
                // Wrap the original exception to provide context, allowing UI to catch specific ones if needed.
                // For example, the UI could catch netDxf.DxfVersionNotSupportedException specifically.
                throw new Exception($"Error loading or parsing DXF file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts entities from a <see cref="DxfDocument"/> into a list of WPF <see cref="System.Windows.Shapes.Shape"/> objects for display.
        /// Supports Lines, Arcs, and LwPolylines.
        /// </summary>
        /// <param name="dxfDocument">The DXF document containing entities to convert.</param>
        /// <returns>A list of WPF <see cref="System.Windows.Shapes.Shape"/> objects. Returns an empty list if the document is null or contains no supported entities.</returns>
        /// <remarks>
        /// The Y-coordinate from DXF (typically positive upwards) is directly mapped to WPF's Y-coordinate
        /// (positive downwards). Canvas transformations in the UI are expected to handle final display orientation (e.g., Y-axis inversion).
        /// Stroke and Fill properties are set minimally (e.g., Fill=Transparent for hit-testing Paths);
        /// final styling (colors, thickness) is expected to be applied in the UI layer (MainWindow.xaml.cs).
        /// </remarks>
        public List<System.Windows.Shapes.Shape> GetWpfShapesFromDxf(DxfDocument dxfDocument)
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape>(); // Qualified List type
            if (dxfDocument == null) return wpfShapes;

            // Process Lines
            foreach (netDxf.Entities.Line dxfLine in dxfDocument.Entities.Lines) // Corrected access
            {
                var wpfLine = new System.Windows.Shapes.Line // Already qualified
                {
                    X1 = dxfLine.StartPoint.X,
                    Y1 = dxfLine.StartPoint.Y, // Direct Y mapping
                    X2 = dxfLine.EndPoint.X,
                    Y2 = dxfLine.EndPoint.Y,
                    IsHitTestVisible = true // Ensure lines are clickable
                };
                wpfShapes.Add(wpfLine);
            }

            // Process Arcs
            foreach (netDxf.Entities.Arc dxfArc in dxfDocument.Entities.Arcs) // Corrected access
            {
                Point startPoint = new Point(dxfArc.StartPoint.X, dxfArc.StartPoint.Y);
                Point endPoint = new Point(dxfArc.EndPoint.X, dxfArc.EndPoint.Y);
                double radius = dxfArc.Radius;

                SweepDirection sweepDirection = (dxfArc.Normal.Z >= 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepDirection == SweepDirection.Clockwise && sweepAngleDegrees > 0) sweepAngleDegrees -= 360;
                if (sweepDirection == SweepDirection.Counterclockwise && sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                bool isLargeArc = Math.Abs(sweepAngleDegrees) > 180.0;

                ArcSegment arcSegment = new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, sweepDirection, true);
                PathFigure pathFigure = new PathFigure { StartPoint = startPoint, IsClosed = false };
                pathFigure.Segments.Add(arcSegment);
                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                var wpfPath = new System.Windows.Shapes.Path // Qualified Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfPath);
            }

            // Process LwPolylines
            foreach (netDxf.Entities.LwPolyline dxfPolyline in dxfDocument.Entities.LwPolylines) // Corrected access
            {
                if (dxfPolyline.Vertices.Count < 1) continue;

                PathFigure pathFigure = new PathFigure();
                pathFigure.StartPoint = new Point(dxfPolyline.Vertices[0].Position.X, dxfPolyline.Vertices[0].Position.Y);
                pathFigure.IsClosed = dxfPolyline.IsClosed;

                if (dxfPolyline.Vertices.Count == 1)
                {
                    continue;
                }

                for (int i = 0; i < dxfPolyline.Vertices.Count; i++)
                {
                    var startVertex = dxfPolyline.Vertices[i];
                    var endVertexInfo = dxfPolyline.IsClosed ? dxfPolyline.Vertices[(i + 1) % dxfPolyline.Vertices.Count] :
                                         (i < dxfPolyline.Vertices.Count - 1 ? dxfPolyline.Vertices[i + 1] : null);
                    if (endVertexInfo == null && !dxfPolyline.IsClosed) break;
                    Point endPoint = new Point(endVertexInfo.Position.X, endVertexInfo.Position.Y);

                    if (Math.Abs(startVertex.Bulge) > 0.0001)
                    {
                        LineSegment lineSegment = new LineSegment(endPoint, true);
                        pathFigure.Segments.Add(lineSegment);
                    }
                    else
                    {
                        LineSegment lineSegment = new LineSegment(endPoint, true);
                        pathFigure.Segments.Add(lineSegment);
                    }
                }

                if (pathFigure.Segments.Any() || dxfPolyline.Vertices.Count == 1)
                {
                    PathGeometry pathGeometry = new PathGeometry();
                    pathGeometry.Figures.Add(pathFigure);
                    var wpfPath = new System.Windows.Shapes.Path // Qualified Path
                    {
                        Data = pathGeometry,
                        Fill = dxfPolyline.IsClosed ? Brushes.Transparent : null,
                        IsHitTestVisible = true
                    };
                    wpfShapes.Add(wpfPath);
                }
            }
            return wpfShapes;
        }

        /// <summary>
        /// Converts a DXF Line entity into a list of two points (start and end).
        /// </summary>
        public List<Point> ConvertLineToPoints(netDxf.Entities.Line line)
        {
            var points = new List<Point>();
            if (line == null) return points;
            points.Add(new Point(line.StartPoint.X, line.StartPoint.Y));
            points.Add(new Point(line.EndPoint.X, line.EndPoint.Y));
            return points;
        }

        /// <summary>
        /// Converts a DXF Arc entity into a list of discretized points.
        /// </summary>
        public List<Point> ConvertArcToPoints(netDxf.Entities.Arc arc, double resolutionDegrees)
        {
            var points = new List<Point>();
            if (arc == null || resolutionDegrees <= 0) return points;
            double startAngleRad = arc.StartAngle * Math.PI / 180.0;
            double endAngleRad = arc.EndAngle * Math.PI / 180.0;
            double radius = arc.Radius;
            Point center = new Point(arc.Center.X, arc.Center.Y);
            bool isCounterClockwise = arc.Normal.Z >= 0;
            if (!isCounterClockwise) { while (endAngleRad > startAngleRad) endAngleRad -= 2 * Math.PI; }
            else { while (endAngleRad < startAngleRad) endAngleRad += 2 * Math.PI; }
            if (Math.Abs(arc.StartAngle - arc.EndAngle) < 0.0001 && Math.Abs(startAngleRad - endAngleRad) < 0.0001 * (Math.PI/180.0) ) {
                 endAngleRad = startAngleRad + (isCounterClockwise ? (2 * Math.PI) : (-2 * Math.PI)); }
            double stepRad = resolutionDegrees * Math.PI / 180.0;
            if (!isCounterClockwise) stepRad = -stepRad;
            double currentAngleRad = startAngleRad;
            bool continueLoop = true;
            while(continueLoop) {
                points.Add(new Point(center.X + radius * Math.Cos(currentAngleRad), center.Y + radius * Math.Sin(currentAngleRad)));
                if (isCounterClockwise) { if (currentAngleRad >= endAngleRad - Math.Abs(stepRad) * 0.1) continueLoop = false; }
                else { if (currentAngleRad <= endAngleRad + Math.Abs(stepRad) * 0.1) continueLoop = false; }
                if(continueLoop) currentAngleRad += stepRad;
            }
            Point finalDxfEndPoint = new Point(arc.EndPoint.X, arc.EndPoint.Y);
            if (!points.Any() || Point.Subtract(points.Last(), finalDxfEndPoint).Length > 0.001) {
                if (points.Any() && Point.Subtract(points.Last(), finalDxfEndPoint).Length < resolutionDegrees * (Math.PI/180.0) * radius * 0.5) {
                    points[points.Count -1] = finalDxfEndPoint; } else { points.Add(finalDxfEndPoint); }
            }
            return points;
        }

        /// <summary>
        /// Converts a DXF LwPolyline entity into a list of discretized points.
        /// </summary>
        public List<Point> ConvertLwPolylineToPoints(netDxf.Entities.LwPolyline polyline, double arcResolutionDegrees)
        {
            var points = new List<Point>();
            if (polyline == null || polyline.Vertices.Count == 0) return points;
            for (int i = 0; i < polyline.Vertices.Count; i++) {
                var currentVertexInfo = polyline.Vertices[i];
                Point currentDxfPoint = new Point(currentVertexInfo.Position.X, currentVertexInfo.Position.Y);
                points.Add(currentDxfPoint);
                if (Math.Abs(currentVertexInfo.Bulge) > 0.0001) {
                    if (!polyline.IsClosed && i == polyline.Vertices.Count - 1) continue;
                    // TODO: Implement LwPolyline bulge to Arc conversion.
                }
            }
            if (polyline.IsClosed && points.Count > 1 && Point.Subtract(points.First(), points.Last()).Length > 0.001) {
                // points.Add(points[0]); // Potentially re-add first point if robot needs explicit closure for trajectories
            } else if (polyline.Vertices.Count == 1 && !points.Any()){
                 points.Add(new Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }
            return points;
        }
    }
}
