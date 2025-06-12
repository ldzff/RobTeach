using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units; // Required for AngleUnit
using System;
using System.Collections.Generic;
using System.IO; // Required for File.Exists
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobTeach.Services
{
    public class CadService
    {
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
                // Consider logging the exception
                throw new Exception($"Error loading DXF file: {ex.Message}", ex);
            }
        }

        public List<Shape> GetWpfShapesFromDxf(DxfDocument dxfDocument)
        {
            var wpfShapes = new List<Shape>();
            if (dxfDocument == null) return wpfShapes;

            // Process Lines
            foreach (netDxf.Entities.Line dxfLine in dxfDocument.Lines)
            {
                var wpfLine = new System.Windows.Shapes.Line
                {
                    X1 = dxfLine.StartPoint.X,
                    Y1 = dxfLine.StartPoint.Y,
                    X2 = dxfLine.EndPoint.X,
                    Y2 = dxfLine.EndPoint.Y,
                    // Stroke is set in MainWindow
                    // StrokeThickness is set in MainWindow
                    IsHitTestVisible = true // Default, but explicit
                };
                wpfShapes.Add(wpfLine);
            }

            // Process Arcs
            foreach (netDxf.Entities.Arc dxfArc in dxfDocument.Arcs)
            {
                Point startPoint = new Point(dxfArc.StartPoint.X, dxfArc.StartPoint.Y);
                Point endPoint = new Point(dxfArc.EndPoint.X, dxfArc.EndPoint.Y);
                double radius = dxfArc.Radius;
                // CAD systems like AutoCAD define arcs counter-clockwise.
                // WPF's ArcSegment sweep direction depends on how angles are interpreted.
                // If angles are always 0 to 360 (or 0 to 2PI), then:
                // SweepDirection = (endAngle > startAngle) ? Counterclockwise : Clockwise;
                // However, DXF angles can cross 360.
                // A common way is to use the Normal vector. DXF default normal (0,0,1) means CCW.
                SweepDirection sweepDirection = (dxfArc.Normal.Z >= 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

                // isLargeArc calculation: an arc is large if its sweep is > 180 degrees.
                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepDirection == SweepDirection.Clockwise && sweepAngleDegrees > 0) sweepAngleDegrees -= 360;
                if (sweepDirection == SweepDirection.Counterclockwise && sweepAngleDegrees < 0) sweepAngleDegrees += 360;

                bool isLargeArc = Math.Abs(sweepAngleDegrees) > 180.0;

                ArcSegment arcSegment = new ArcSegment(
                    endPoint,
                    new Size(radius, radius),
                    0, // rotationAngle - typically 0 for circular arcs from DXF
                    isLargeArc,
                    sweepDirection,
                    true // isStroked
                );

                PathFigure pathFigure = new PathFigure { StartPoint = startPoint };
                pathFigure.Segments.Add(arcSegment);
                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                var wpfPath = new Path
                {
                    Data = pathGeometry,
                    // Stroke, StrokeThickness set in MainWindow
                    Fill = Brushes.Transparent, // Important for hit testing on the area of the arc
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfPath);
            }

            // Process LwPolylines
            foreach (netDxf.Entities.LwPolyline dxfPolyline in dxfDocument.LwPolylines)
            {
                if (dxfPolyline.Vertices.Count < 1) continue; // Need at least one vertex for a point, two for a line

                PathFigure pathFigure = new PathFigure();
                pathFigure.StartPoint = new Point(dxfPolyline.Vertices[0].Position.X, dxfPolyline.Vertices[0].Position.Y);

                if (dxfPolyline.Vertices.Count == 1) // It's a point, represent as a tiny circle or skip
                {
                    // Or create a small circle Path for a single vertex polyline point
                    // For now, skipping single-vertex polylines from shape generation
                    // Or handle as a small dot if necessary:
                    // EllipseGeometry pointGeometry = new EllipseGeometry(pathFigure.StartPoint, 0.5, 0.5);
                    // var wpfPointPath = new Path { Data = pointGeometry, Fill = Brushes.Black, IsHitTestVisible = true };
                    // wpfShapes.Add(wpfPointPath);
                    continue;
                }


                for (int i = 0; i < dxfPolyline.Vertices.Count; i++)
                {
                    var startVertex = dxfPolyline.Vertices[i];
                    // For the last vertex of an open polyline, or any vertex of a closed one that leads to the next
                    var nextVertexIndex = (i + 1) % dxfPolyline.Vertices.Count;

                    if (!dxfPolyline.IsClosed && i == dxfPolyline.Vertices.Count - 1) // Last vertex of an open polyline
                    {
                        // If it's just a straight polyline, the segments are already added up to this point.
                        // If the polyline has only one segment, this loop might not behave as expected.
                        // The logic should handle adding segments between vertex i and vertex i+1.
                        // This loop structure might need refinement for clarity.
                        break;
                    }
                    var endVertexInfo = dxfPolyline.Vertices[nextVertexIndex];
                    Point endPoint = new Point(endVertexInfo.Position.X, endVertexInfo.Position.Y);

                    if (Math.Abs(startVertex.Bulge) > 0.0001) // Arc segment due to bulge
                    {
                        // TODO: Implement proper LwPolyline bulge to ArcSegment conversion
                        // For now, drawing a straight line segment for bulged segments
                        LineSegment lineSegment = new LineSegment(endPoint, true /* isStroked */);
                        pathFigure.Segments.Add(lineSegment);
                    }
                    else // Straight segment
                    {
                        LineSegment lineSegment = new LineSegment(endPoint, true /* isStroked */);
                        pathFigure.Segments.Add(lineSegment);
                    }
                     // If it's the last segment of a closed polyline, it's already handled by % operator.
                }

                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);
                var wpfPath = new Path
                {
                    Data = pathGeometry,
                    // Stroke, StrokeThickness set in MainWindow
                    Fill = dxfPolyline.IsClosed ? Brushes.Transparent : null, // Transparent fill for closed polylines for hit testing
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfPath);
            }
            return wpfShapes;
        }

        // Trajectory Generation Methods (no changes in this step)
        public List<Point> ConvertLineToPoints(netDxf.Entities.Line line)
        {
            var points = new List<Point>();
            if (line == null) return points;
            points.Add(new Point(line.StartPoint.X, line.StartPoint.Y));
            points.Add(new Point(line.EndPoint.X, line.EndPoint.Y));
            return points;
        }

        public List<Point> ConvertArcToPoints(netDxf.Entities.Arc arc, double resolutionDegrees)
        {
            var points = new List<Point>();
            if (arc == null || resolutionDegrees <= 0) return points;

            double startAngleRad = arc.StartAngle * Math.PI / 180.0;
            double endAngleRad = arc.EndAngle * Math.PI / 180.0;
            double radius = arc.Radius;
            Point center = new Point(arc.Center.X, arc.Center.Y);

            if (arc.Normal.Z < 0)
            {
                if (endAngleRad > startAngleRad) endAngleRad -= 2 * Math.PI;
            }
            else
            {
                 if (endAngleRad < startAngleRad) endAngleRad += 2 * Math.PI;
            }

            // If start and end angles are effectively the same, it might be a full circle in some DXF conventions
            if (Math.Abs(arc.StartAngle - arc.EndAngle) < 0.0001 && Math.Abs(startAngleRad - endAngleRad) < 0.0001) {
                 // Check if DXF represents it as 0 to 0 for full circle, or if netDxf normalizes it.
                 // For now, assume if angles are same, it's not a zero-length arc but a full circle.
                 endAngleRad = startAngleRad + (arc.Normal.Z >= 0 ? (2 * Math.PI) : (-2 * Math.PI));
            }


            double stepRad = resolutionDegrees * Math.PI / 180.0;
            stepRad = arc.Normal.Z >= 0 ? stepRad : -stepRad; // Adjust step direction

            for (double currentAngleRad = startAngleRad;
                 arc.Normal.Z >= 0 ? currentAngleRad <= endAngleRad + stepRad*0.1 : currentAngleRad >= endAngleRad + stepRad*0.1; // Add/sub small tolerance
                 currentAngleRad += stepRad)
            {
                double angleToUse = currentAngleRad;
                if(arc.Normal.Z >=0 && currentAngleRad > endAngleRad) angleToUse = endAngleRad;
                if(arc.Normal.Z < 0 && currentAngleRad < endAngleRad) angleToUse = endAngleRad;

                double x = center.X + radius * Math.Cos(angleToUse);
                double y = center.Y + radius * Math.Sin(angleToUse);
                points.Add(new Point(x, y));

                if(angleToUse == endAngleRad) break;
            }

            // Ensure the very last point is exactly the arc's end point
            Point finalDxfEndPoint = new Point(arc.EndPoint.X, arc.EndPoint.Y);
            if (!points.Any() || Point.Subtract(points.Last(), finalDxfEndPoint).Length > 0.001)
            {
                if(points.Any() && Point.Subtract(points.First(), finalDxfEndPoint).Length < 0.001 && points.Count > 1) {
                     // If it's a full circle and last point is close to first, remove last to avoid duplicate with ensured end point
                    if(Point.Subtract(points.Last(), points.First()).Length < 0.001) points.RemoveAt(points.Count -1);
                }
                points.Add(finalDxfEndPoint);
            }


            return points;
        }

        public List<Point> ConvertLwPolylineToPoints(netDxf.Entities.LwPolyline polyline, double arcResolutionDegrees)
        {
            var points = new List<Point>();
            if (polyline == null || polyline.Vertices.Count == 0) return points;

            for (int i = 0; i < polyline.Vertices.Count; i++)
            {
                var currentVertexInfo = polyline.Vertices[i];
                Point currentDxfPoint = new Point(currentVertexInfo.Position.X, currentVertexInfo.Position.Y);

                if (Math.Abs(currentVertexInfo.Bulge) > 0.0001)
                {
                    var nextVertexIndex = (i + 1) % polyline.Vertices.Count;
                    if (!polyline.IsClosed && i == polyline.Vertices.Count - 1) { // Last vertex of open polyline, no bulge from here
                        points.Add(currentDxfPoint);
                        break;
                    }
                    var nextVertexInfo = polyline.Vertices[nextVertexIndex];
                    Point nextDxfPoint = new Point(nextVertexInfo.Position.X, nextVertexInfo.Position.Y);

                    // TODO: Replace with proper bulge arc conversion using LwPolylineVertex.BulgeToArcParameters if available
                    // and then calling ConvertArcToPoints.
                    // Simplified: add current point, effectively treating bulge as straight line for trajectory points.
                    points.Add(currentDxfPoint);
                }
                else
                {
                    points.Add(currentDxfPoint);
                }
            }

            if (polyline.IsClosed && points.Count > 1 && Point.Subtract(points.First(), points.Last()).Length > 0.001)
            {
                points.Add(points[0]); // Ensure closure for trajectory
            }
             else if (polyline.Vertices.Count == 1 && !points.Any()){ // Single point polyline
                 points.Add(new Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
             }


            return points;
        }
    }
}
