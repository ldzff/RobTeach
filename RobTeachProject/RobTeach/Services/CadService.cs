using netDxf;
using netDxf.Entities;
// using netDxf.Tables; // Currently not used directly, consider removing if not needed
// using netDxf.Units; // Currently not used directly, consider removing if not needed
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows; // Retain for Point, Size, Rect if used, or qualify all
using System.Windows.Media;
// using System.Windows.Shapes; // All Shape usages are qualified

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

            // Process Lines
            foreach (netDxf.Entities.Line dxfLine in dxfDocument.Entities.Lines)
            {
                var wpfLine = new System.Windows.Shapes.Line
                {
                    X1 = dxfLine.StartPoint.X,
                    Y1 = dxfLine.StartPoint.Y,
                    X2 = dxfLine.EndPoint.X,
                    Y2 = dxfLine.EndPoint.Y,
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfLine);
            }

            // Process Arcs
            foreach (netDxf.Entities.Arc dxfArc in dxfDocument.Entities.Arcs)
            {
                // Calculate Arc Start Point using DXF properties
                double startAngleRad = dxfArc.StartAngle * Math.PI / 180.0;
                double arcStartX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(startAngleRad);
                double arcStartY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(startAngleRad);
                var pathStartPoint = new System.Windows.Point(arcStartX, arcStartY);

                // Calculate Arc End Point for ArcSegment.Point property
                double endAngleRad = dxfArc.EndAngle * Math.PI / 180.0;
                double arcEndX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(endAngleRad);
                double arcEndY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(endAngleRad);
                var arcSegmentEndPoint = new System.Windows.Point(arcEndX, arcEndY);

                double radius = dxfArc.Radius;
                SweepDirection sweepDirection = (dxfArc.Normal.Z >= 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

                // Calculate total angle sweep for IsLargeArc.
                // Ensure angles are handled correctly if they cross 0/360 boundary.
                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepDirection == SweepDirection.Clockwise && sweepAngleDegrees > 0) sweepAngleDegrees -= 360;
                else if (sweepDirection == SweepDirection.Counterclockwise && sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                // If Normal.Z is negative, the interpretation of IsLargeArc might need to be flipped
                // relative to the absolute sweep angle if WPF's SweepDirection handles the primary directionality.
                // For now, using Math.Abs.
                bool isLargeArc = Math.Abs(sweepAngleDegrees) > 180.0;


                ArcSegment arcSegment = new ArcSegment
                {
                    Point = arcSegmentEndPoint, // Use calculated end point
                    Size = new System.Windows.Size(radius, radius),
                    IsLargeArc = isLargeArc,
                    SweepDirection = sweepDirection,
                    RotationAngle = 0, // DXF Arcs are circular, so no ellipse rotation.
                    IsStroked = true
                };

                PathFigure pathFigure = new PathFigure
                {
                    StartPoint = pathStartPoint, // Use calculated start point
                    IsClosed = false
                };
                pathFigure.Segments.Add(arcSegment);
                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                var wpfPath = new System.Windows.Shapes.Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfPath);
            }

            // Process LwPolylines
            foreach (netDxf.Entities.LwPolyline dxfPolyline in dxfDocument.Entities.LwPolylines)
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
                        // TODO: Implement proper LwPolyline bulge to ArcSegment conversion.
                        LineSegment lineSegment = new LineSegment(endPoint, true);
                        pathFigure.Segments.Add(lineSegment);
                    }
                    else
                    {
                        LineSegment lineSegment = new LineSegment(endPoint, true);
                        pathFigure.Segments.Add(lineSegment);
                    }
                }

                if (pathFigure.Segments.Any()) // Only add if segments were created
                {
                    PathGeometry pathGeometry = new PathGeometry();
                    pathGeometry.Figures.Add(pathFigure);
                    var wpfPath = new System.Windows.Shapes.Path
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
            System.Windows.Point center = new System.Windows.Point(arc.Center.X, arc.Center.Y); // Qualified Point
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
                if(continueLoop) currentAngleRad += stepRad; else currentAngleRad = endAngleRad; // Ensure last point is at endAngle
            }
            // Ensure the final point is exactly the calculated end point of the arc if not already added.
            // This handles cases where the loop terminates slightly off due to floating point arithmetic.
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
        /// Converts a DXF LwPolyline entity into a list of discretized System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertLwPolylineToPoints(netDxf.Entities.LwPolyline polyline, double arcResolutionDegrees)
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
                 points.Add(points[0]); // Explicitly close the trajectory path for closed polylines
            } else if (polyline.Vertices.Count == 1 && !points.Any()){
                 points.Add(new System.Windows.Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }
            return points;
        }
    }
}
