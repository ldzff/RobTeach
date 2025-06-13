using netDxf;
using netDxf.Entities;
// using netDxf.Tables;
// using netDxf.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Linq;
// using System.Windows.Shapes;

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
        public DxfDocument LoadDxf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("DXF file not found.", filePath);
            }
            try
            {
                DxfDocument dxf = DxfDocument.Load(filePath);
                return dxf;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading or parsing DXF file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Converts entities from a <see cref="DxfDocument"/> into a list of WPF <see cref="System.Windows.Shapes.Shape"/> objects for display.
        /// Supports Lines, Arcs, and Circles. LwPolyline processing is removed.
        /// </summary>
        public List<System.Windows.Shapes.Shape> GetWpfShapesFromDxf(DxfDocument dxfDocument)
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape>();
            if (dxfDocument == null) return wpfShapes;

            // Process Lines
            foreach (netDxf.Entities.Line dxfLine in dxfDocument.Entities.Lines)
            {
                var wpfLine = new System.Windows.Shapes.Line
                {
                    X1 = dxfLine.StartPoint.X, Y1 = dxfLine.StartPoint.Y,
                    X2 = dxfLine.EndPoint.X, Y2 = dxfLine.EndPoint.Y,
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfLine);
            }

            // Process Arcs
            foreach (netDxf.Entities.Arc dxfArc in dxfDocument.Entities.Arcs)
            {
                double startAngleRad = dxfArc.StartAngle * Math.PI / 180.0;
                double arcStartX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(startAngleRad);
                double arcStartY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(startAngleRad);
                var pathStartPoint = new System.Windows.Point(arcStartX, arcStartY);

                double endAngleRad = dxfArc.EndAngle * Math.PI / 180.0;
                double arcEndX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(endAngleRad);
                double arcEndY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(endAngleRad);
                var arcSegmentEndPoint = new System.Windows.Point(arcEndX, arcEndY);

                double radius = dxfArc.Radius;
                SweepDirection sweepDirection = (dxfArc.Normal.Z >= 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepDirection == SweepDirection.Clockwise && sweepAngleDegrees > 0) sweepAngleDegrees -= 360;
                else if (sweepDirection == SweepDirection.Counterclockwise && sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                bool isLargeArc = Math.Abs(sweepAngleDegrees) > 180.0;

                ArcSegment arcSegment = new ArcSegment
                {
                    Point = arcSegmentEndPoint, Size = new System.Windows.Size(radius, radius),
                    IsLargeArc = isLargeArc, SweepDirection = sweepDirection,
                    RotationAngle = 0, IsStroked = true
                };
                PathFigure pathFigure = new PathFigure { StartPoint = pathStartPoint, IsClosed = false };
                pathFigure.Segments.Add(arcSegment);
                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);
                var wpfPath = new System.Windows.Shapes.Path { Data = pathGeometry, Fill = Brushes.Transparent, IsHitTestVisible = true };
                wpfShapes.Add(wpfPath);
            }

            // Process Circles
            foreach (netDxf.Entities.Circle dxfCircle in dxfDocument.Entities.Circles)
            {
                var ellipseGeometry = new EllipseGeometry(
                    new System.Windows.Point(dxfCircle.Center.X, dxfCircle.Center.Y),
                    dxfCircle.Radius,
                    dxfCircle.Radius
                );
                var circlePath = new System.Windows.Shapes.Path
                {
                    // Stroke and StrokeThickness will be set in MainWindow
                    Data = ellipseGeometry,
                    Fill = Brushes.Transparent, // For hit-testing
                    IsHitTestVisible = true
                    // Tag = dxfCircle // Optionally store original entity if needed for mapping, though MainWindow maps by shape instance
                };
                wpfShapes.Add(circlePath);
            }

            /* // LightWeightPolyline processing removed as per subtask
            foreach (LightWeightPolyline dxfPolyline in dxfDocument.Entities.LwPolylines)
            {
                // ... logic for LwPolylines was here ...
            }
            */
            return wpfShapes;
        }

        /// <summary>
        /// Converts a DXF Line entity into a list of two System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertLineToPoints(netDxf.Entities.Line line)
        {
            var points = new List<System.Windows.Point>();
            if (line == null) return points;
            points.Add(new System.Windows.Point(line.StartPoint.X, line.StartPoint.Y));
            points.Add(new System.Windows.Point(line.EndPoint.X, line.EndPoint.Y));
            return points;
        }

        /// <summary>
        /// Converts a DXF Arc entity into a list of discretized System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertArcToPoints(netDxf.Entities.Arc arc, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (arc == null || resolutionDegrees <= 0) return points;
            double startAngleRad = arc.StartAngle * Math.PI / 180.0;
            double endAngleRad = arc.EndAngle * Math.PI / 180.0;
            double radius = arc.Radius;
            System.Windows.Point center = new System.Windows.Point(arc.Center.X, arc.Center.Y);
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
                points.Add(new System.Windows.Point(center.X + radius * Math.Cos(currentAngleRad),
                                     center.Y + radius * Math.Sin(currentAngleRad)));
                if (isCounterClockwise) { if (currentAngleRad >= endAngleRad - Math.Abs(stepRad) * 0.1) continueLoop = false; }
                else { if (currentAngleRad <= endAngleRad + Math.Abs(stepRad) * 0.1) continueLoop = false; }
                if(continueLoop) currentAngleRad += stepRad; else currentAngleRad = endAngleRad;
            }
            System.Windows.Point calculatedArcEndPoint = new System.Windows.Point(
                center.X + radius * Math.Cos(endAngleRad),
                center.Y + radius * Math.Sin(endAngleRad)
            );
            if (!points.Any() || System.Windows.Point.Subtract(points.Last(), calculatedArcEndPoint).Length > 0.001) {
                 if (points.Any() && System.Windows.Point.Subtract(points.Last(), calculatedArcEndPoint).Length < Math.Abs(stepRad) * radius * 0.5) {
                    points[points.Count -1] = calculatedArcEndPoint;
                } else {
                    points.Add(calculatedArcEndPoint);
                }
            }
            return points;
        }

        /// <summary>
        /// Converts a netDxf Circle entity to a list of points representing its perimeter.
        /// </summary>
        /// <param name="circle">The DXF Circle entity.</param>
        /// <param name="resolutionDegrees">The angular step in degrees for discretizing the circle.</param>
        /// <returns>A list of System.Windows.Point for the circle's trajectory.</returns>
        public List<System.Windows.Point> ConvertCircleToPoints(netDxf.Entities.Circle circle, double resolutionDegrees)
        {
            List<System.Windows.Point> points = new List<System.Windows.Point>();
            if (circle == null || resolutionDegrees <= 0) return points;

            for (double angle = 0; angle < 360.0; angle += resolutionDegrees)
            {
                double radAngle = angle * Math.PI / 180.0;
                double x = circle.Center.X + circle.Radius * Math.Cos(radAngle);
                double y = circle.Center.Y + circle.Radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
            }
            // Add the first point again to close the circle visually if it's a polyline preview
            if (points.Any()) // Requires System.Linq
            {
                 points.Add(points[0]);
            }
            return points;
        }

        /* // LightWeightPolyline processing removed as per subtask
        /// <summary>
        /// Converts a DXF LwPolyline entity into a list of discretized System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertLwPolylineToPoints(LightWeightPolyline polyline, double arcResolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (polyline == null || polyline.Vertices.Count == 0) return points;
            for (int i = 0; i < polyline.Vertices.Count; i++) {
                var currentVertexInfo = polyline.Vertices[i];
                System.Windows.Point currentDxfPoint = new System.Windows.Point(currentVertexInfo.Position.X, currentVertexInfo.Position.Y);
                points.Add(currentDxfPoint);
                if (Math.Abs(currentVertexInfo.Bulge) > 0.0001) {
                    if (!polyline.IsClosed && i == polyline.Vertices.Count - 1) continue;
                    // TODO: Implement LwPolyline bulge to Arc conversion for trajectory points.
                }
            }
            if (polyline.IsClosed && points.Count > 1 && System.Windows.Point.Subtract(points.First(), points.Last()).Length > 0.001) { // Requires System.Linq for .First() and .Last()
                 points.Add(points[0]);
            } else if (polyline.Vertices.Count == 1 && !points.Any()){ // Requires System.Linq for .Any()
                 points.Add(new System.Windows.Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }
            return points;
        }
        */
    }
}
