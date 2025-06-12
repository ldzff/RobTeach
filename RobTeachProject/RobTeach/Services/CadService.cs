using netDxf;
using netDxf.Entities;
// using netDxf.Tables; // Currently not used directly, consider removing if not needed
// using netDxf.Units; // Currently not used directly, consider removing if not needed
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
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
        /// </summary>
        public List<System.Windows.Shapes.Shape> GetWpfShapesFromDxf(DxfDocument dxfDocument)
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape>();
            if (dxfDocument == null) return wpfShapes;

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

            // Corrected: LightWeightPolyline
            foreach (LightWeightPolyline dxfPolyline in dxfDocument.Entities.LwPolylines)
            {
                if (dxfPolyline.Vertices.Count < 1) continue;
                PathFigure pathFigure = new PathFigure();
                pathFigure.StartPoint = new System.Windows.Point(dxfPolyline.Vertices[0].Position.X, dxfPolyline.Vertices[0].Position.Y);
                pathFigure.IsClosed = dxfPolyline.IsClosed;
                if (dxfPolyline.Vertices.Count == 1) continue;

                for (int i = 0; i < dxfPolyline.Vertices.Count; i++)
                {
                    var startVertex = dxfPolyline.Vertices[i];
                    var endVertexInfo = dxfPolyline.IsClosed ? dxfPolyline.Vertices[(i + 1) % dxfPolyline.Vertices.Count] :
                                         (i < dxfPolyline.Vertices.Count - 1 ? dxfPolyline.Vertices[i + 1] : null);
                    if (endVertexInfo == null && !dxfPolyline.IsClosed) break;
                    System.Windows.Point endPoint = new System.Windows.Point(endVertexInfo.Position.X, endVertexInfo.Position.Y);

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
                if (pathFigure.Segments.Any())
                {
                    PathGeometry pathGeometry = new PathGeometry();
                    pathGeometry.Figures.Add(pathFigure);
                    var wpfPath = new System.Windows.Shapes.Path { Data = pathGeometry, Fill = dxfPolyline.IsClosed ? Brushes.Transparent : null, IsHitTestVisible = true };
                    wpfShapes.Add(wpfPath);
                }
            }
            return wpfShapes;
        }

        public List<System.Windows.Point> ConvertLineToPoints(netDxf.Entities.Line line)
        {
            var points = new List<System.Windows.Point>();
            if (line == null) return points;
            points.Add(new System.Windows.Point(line.StartPoint.X, line.StartPoint.Y));
            points.Add(new System.Windows.Point(line.EndPoint.X, line.EndPoint.Y));
            return points;
        }

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
                points.Add(new System.Windows.Point(center.X + radius * Math.Cos(currentAngleRad), center.Y + radius * Math.Sin(currentAngleRad)));
                if (isCounterClockwise) { if (currentAngleRad >= endAngleRad - Math.Abs(stepRad) * 0.1) continueLoop = false; }
                else { if (currentAngleRad <= endAngleRad + Math.Abs(stepRad) * 0.1) continueLoop = false; }
                if(continueLoop) currentAngleRad += stepRad; else currentAngleRad = endAngleRad;
            }
            System.Windows.Point calculatedArcEndPoint = new System.Windows.Point(center.X + radius * Math.Cos(endAngleRad), center.Y + radius * Math.Sin(endAngleRad));
            if (!points.Any() || System.Windows.Point.Subtract(points.Last(), calculatedArcEndPoint).Length > 0.001) {
                 if (points.Any() && System.Windows.Point.Subtract(points.Last(), calculatedArcEndPoint).Length < Math.Abs(stepRad) * radius * 0.5) {
                    points[points.Count -1] = calculatedArcEndPoint; } else { points.Add(calculatedArcEndPoint); }
            }
            return points;
        }

        // Corrected: LightWeightPolyline
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
            if (polyline.IsClosed && points.Count > 1 && System.Windows.Point.Subtract(points.First(), points.Last()).Length > 0.001) {
                 points.Add(points[0]);
            } else if (polyline.Vertices.Count == 1 && !points.Any()){
                 points.Add(new System.Windows.Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }
            return points;
        }
    }
}
