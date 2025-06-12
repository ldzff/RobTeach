using netDxf;
using netDxf.Entities;
// using netDxf.Tables; // Currently not used directly, consider removing if not needed
// using netDxf.Units; // Currently not used directly, consider removing if not needed
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

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
        /// Converts entities from a <see cref="DxfDocument"/> into a list of WPF <see cref="Shape"/> objects for display.
        /// Supports Lines, Arcs, and LwPolylines.
        /// </summary>
        /// <param name="dxfDocument">The DXF document containing entities to convert.</param>
        /// <returns>A list of WPF <see cref="Shape"/> objects. Returns an empty list if the document is null or contains no supported entities.</returns>
        /// <remarks>
        /// The Y-coordinate from DXF (typically positive upwards) is directly mapped to WPF's Y-coordinate
        /// (positive downwards). Canvas transformations in the UI are expected to handle final display orientation (e.g., Y-axis inversion).
        /// Stroke and Fill properties are set minimally (e.g., Fill=Transparent for hit-testing Paths);
        /// final styling (colors, thickness) is expected to be applied in the UI layer (MainWindow.xaml.cs).
        /// </remarks>
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
                    Y1 = dxfLine.StartPoint.Y, // Direct Y mapping
                    X2 = dxfLine.EndPoint.X,
                    Y2 = dxfLine.EndPoint.Y,
                    IsHitTestVisible = true // Ensure lines are clickable
                };
                wpfShapes.Add(wpfLine);
            }

            // Process Arcs
            foreach (netDxf.Entities.Arc dxfArc in dxfDocument.Arcs)
            {
                Point startPoint = new Point(dxfArc.StartPoint.X, dxfArc.StartPoint.Y);
                Point endPoint = new Point(dxfArc.EndPoint.X, dxfArc.EndPoint.Y);
                double radius = dxfArc.Radius;

                // Determine sweep direction based on Normal vector (DXF convention: (0,0,1) is CCW).
                SweepDirection sweepDirection = (dxfArc.Normal.Z >= 0) ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;

                // Calculate sweep angle for IsLargeArc. DXF angles are in degrees.
                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                // Adjust sweep angle if it crosses the 0/360 boundary based on direction
                if (sweepDirection == SweepDirection.Clockwise && sweepAngleDegrees > 0) sweepAngleDegrees -= 360;
                if (sweepDirection == SweepDirection.Counterclockwise && sweepAngleDegrees < 0) sweepAngleDegrees += 360;

                bool isLargeArc = Math.Abs(sweepAngleDegrees) > 180.0;

                ArcSegment arcSegment = new ArcSegment(
                    endPoint,
                    new Size(radius, radius),
                    0, // rotationAngle for the ellipse - DXF arcs are circular, so 0.
                    isLargeArc,
                    sweepDirection,
                    true // isStroked
                );

                PathFigure pathFigure = new PathFigure { StartPoint = startPoint, IsClosed = false }; // Arcs are not closed figures themselves
                pathFigure.Segments.Add(arcSegment);
                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                var wpfPath = new Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent, // Crucial for hit-testing the area of the arc
                    IsHitTestVisible = true
                };
                wpfShapes.Add(wpfPath);
            }

            // Process LwPolylines (Lightweight Polylines)
            foreach (netDxf.Entities.LwPolyline dxfPolyline in dxfDocument.LwPolylines)
            {
                if (dxfPolyline.Vertices.Count < 1) continue;

                PathFigure pathFigure = new PathFigure();
                pathFigure.StartPoint = new Point(dxfPolyline.Vertices[0].Position.X, dxfPolyline.Vertices[0].Position.Y);
                pathFigure.IsClosed = dxfPolyline.IsClosed; // Respect the closed status of the polyline

                if (dxfPolyline.Vertices.Count == 1)
                {
                    // Represent a single-vertex LwPolyline as a small dot (e.g., a tiny circle) or skip.
                    // Here, we create a very small circle for visibility and clickability.
                    EllipseGeometry pointMarker = new EllipseGeometry(pathFigure.StartPoint, 0.5, 0.5); // Small radius
                    var wpfPointPath = new Path { Data = pointMarker, Fill = Brushes.Black, IsHitTestVisible = true };
                    // Note: This point won't be selectable in MainWindow unless _wpfShapeToDxfEntityMap logic is extended for these.
                    // For now, these are visual cues, not typically processed for trajectories.
                    // If these need to be selectable, ensure they are added to the map in MainWindow.
                    // wpfShapes.Add(wpfPointPath); // Decided to skip adding single points as selectable shapes for trajectories.
                    continue;
                }

                // Iterate through vertices to create segments
                for (int i = 0; i < dxfPolyline.Vertices.Count; i++)
                {
                    var startVertex = dxfPolyline.Vertices[i];
                    // Determine the end vertex for the segment. If closed, it wraps around. If open, it stops at the last vertex.
                    var endVertexInfo = dxfPolyline.IsClosed ? dxfPolyline.Vertices[(i + 1) % dxfPolyline.Vertices.Count] :
                                         (i < dxfPolyline.Vertices.Count - 1 ? dxfPolyline.Vertices[i + 1] : null);

                    if (endVertexInfo == null && !dxfPolyline.IsClosed) {
                        // This is the last vertex of an open polyline, no more segments to form from it.
                        break;
                    }
                    Point endPoint = new Point(endVertexInfo.Position.X, endVertexInfo.Position.Y);

                    if (Math.Abs(startVertex.Bulge) > 0.0001) // If there is a bulge, it's an arc segment
                    {
                        // TODO: Implement proper LwPolyline bulge to ArcSegment conversion.
                        // This requires calculating arc center, radius, and start/end angles from bulge value, start/end points.
                        // netDxf.LwPolylineVertex.BulgeToArcParameters() can be helpful here.
                        // For now, drawing a straight line segment as a placeholder for bulged segments.
                        LineSegment lineSegment = new LineSegment(endPoint, true /* isStroked */);
                        pathFigure.Segments.Add(lineSegment);
                    }
                    else // Straight segment
                    {
                        LineSegment lineSegment = new LineSegment(endPoint, true /* isStroked */);
                        pathFigure.Segments.Add(lineSegment);
                    }
                }

                // Only add the path if it has segments (i.e., more than one vertex processed)
                if (pathFigure.Segments.Any() || dxfPolyline.Vertices.Count == 1) // Or if it was a single point we decided to draw (currently skipped)
                {
                    PathGeometry pathGeometry = new PathGeometry();
                    pathGeometry.Figures.Add(pathFigure);
                    var wpfPath = new Path
                    {
                        Data = pathGeometry,
                        Fill = dxfPolyline.IsClosed ? Brushes.Transparent : null, // Fill closed for hit-testing, no fill for open
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
        /// <param name="line">The <see cref="netDxf.Entities.Line"/> to convert.</param>
        /// <returns>A list containing the start and end points of the line. Returns an empty list if the line is null.</returns>
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
        /// <param name="arc">The <see cref="netDxf.Entities.Arc"/> to convert.</param>
        /// <param name="resolutionDegrees">The angular resolution (step) in degrees for discretizing the arc.</param>
        /// <returns>A list of <see cref="Point"/> objects representing the discretized arc. Returns an empty list if the arc is null or resolution is invalid.</returns>
        public List<Point> ConvertArcToPoints(netDxf.Entities.Arc arc, double resolutionDegrees)
        {
            var points = new List<Point>();
            if (arc == null || resolutionDegrees <= 0) return points;

            // Convert DXF angles (degrees) to radians for Math functions
            double startAngleRad = arc.StartAngle * Math.PI / 180.0;
            double endAngleRad = arc.EndAngle * Math.PI / 180.0;
            double radius = arc.Radius;
            Point center = new Point(arc.Center.X, arc.Center.Y);

            // Normalize angles and determine sweep direction based on arc's normal vector
            // DXF default normal (0,0,1) means CCW.
            bool isCounterClockwise = arc.Normal.Z >= 0;
            if (!isCounterClockwise) // Clockwise arc
            {
                // Ensure endAngleRad is "after" startAngleRad in clockwise direction
                while (endAngleRad > startAngleRad) endAngleRad -= 2 * Math.PI;
            }
            else // Counter-clockwise arc
            {
                // Ensure endAngleRad is "after" startAngleRad in counter-clockwise direction
                while (endAngleRad < startAngleRad) endAngleRad += 2 * Math.PI;
            }

            // Handle full circles where start and end angles might be the same in DXF
            if (Math.Abs(arc.StartAngle - arc.EndAngle) < 0.0001 && Math.Abs(startAngleRad - endAngleRad) < 0.0001 * (Math.PI/180.0) ) {
                 endAngleRad = startAngleRad + (isCounterClockwise ? (2 * Math.PI) : (-2 * Math.PI));
            }

            double stepRad = resolutionDegrees * Math.PI / 180.0;
            if (!isCounterClockwise) stepRad = -stepRad; // Negative step for clockwise

            // Iterate through the angle sweep
            double currentAngleRad = startAngleRad;
            bool continueLoop = true;
            while(continueLoop)
            {
                points.Add(new Point(center.X + radius * Math.Cos(currentAngleRad),
                                     center.Y + radius * Math.Sin(currentAngleRad)));

                if (isCounterClockwise)
                {
                    if (currentAngleRad >= endAngleRad - Math.Abs(stepRad) * 0.1) // Small tolerance to ensure end point is included
                        continueLoop = false; // About to pass or hit end angle
                }
                else // Clockwise
                {
                    if (currentAngleRad <= endAngleRad + Math.Abs(stepRad) * 0.1) // Small tolerance
                        continueLoop = false; // About to pass or hit end angle
                }

                if(continueLoop) currentAngleRad += stepRad;
                else if (Math.Abs(currentAngleRad - endAngleRad) > 0.00001) // If not yet at end point, add it
                {
                    // Ensure last point is exactly the arc's defined end point if loop terminates early.
                    // However, the current loop structure should hit it or be very close.
                    // This specific logic might be refined if issues with exact end point matching arise.
                }
            }

            // Ensure the final DXF-defined end point is added if not already the last point (due to discretization)
            Point finalDxfEndPoint = new Point(arc.EndPoint.X, arc.EndPoint.Y);
            if (!points.Any() || Point.Subtract(points.Last(), finalDxfEndPoint).Length > 0.001)
            {
                // If the list is not empty and the last point is already very close to the true end point,
                // replace it to ensure precision, or just add if significantly different.
                if (points.Any() && Point.Subtract(points.Last(), finalDxfEndPoint).Length < resolutionDegrees * (Math.PI/180.0) * radius * 0.5) { // Heuristic
                    points[points.Count -1] = finalDxfEndPoint; // Correct last point
                } else {
                    points.Add(finalDxfEndPoint);
                }
            }
            return points;
        }

        /// <summary>
        /// Converts a DXF LwPolyline entity into a list of discretized points.
        /// Handles straight segments and placeholder logic for bulged (arc) segments.
        /// </summary>
        /// <param name="polyline">The <see cref="netDxf.Entities.LwPolyline"/> to convert.</param>
        /// <param name="arcResolutionDegrees">The angular resolution for discretizing any bulged segments (if implemented).</param>
        /// <returns>A list of <see cref="Point"/> objects representing the polyline. Returns an empty list if the polyline is null or has no vertices.</returns>
        public List<Point> ConvertLwPolylineToPoints(netDxf.Entities.LwPolyline polyline, double arcResolutionDegrees)
        {
            var points = new List<Point>();
            if (polyline == null || polyline.Vertices.Count == 0) return points;

            for (int i = 0; i < polyline.Vertices.Count; i++)
            {
                var currentVertexInfo = polyline.Vertices[i];
                Point currentDxfPoint = new Point(currentVertexInfo.Position.X, currentVertexInfo.Position.Y);

                points.Add(currentDxfPoint); // Add the vertex itself

                // If there's a bulge, it implies an arc segment to the *next* vertex.
                if (Math.Abs(currentVertexInfo.Bulge) > 0.0001)
                {
                    // Determine the next vertex
                    var nextVertexIndex = (i + 1) % polyline.Vertices.Count;
                    // If it's an open polyline and this is the last vertex, there's no "next" segment defined by its bulge.
                    if (!polyline.IsClosed && i == polyline.Vertices.Count - 1) {
                        continue;
                    }
                    // var nextVertexInfo = polyline.Vertices[nextVertexIndex];
                    // Point nextDxfPoint = new Point(nextVertexInfo.Position.X, nextVertexInfo.Position.Y);

                    // TODO: Implement LwPolyline bulge to Arc conversion.
                    // This involves:
                    // 1. Calculating arc parameters (center, radius, start/end angles) from the two vertices and the bulge factor.
                    //    netDxf.LwPolylineVertex.BulgeToArcParameters(currentVertexInfo.Position, nextVertexInfo.Position, currentVertexInfo.Bulge) can do this.
                    // 2. Creating a temporary netDxf.Entities.Arc with these parameters.
                    // 3. Calling ConvertArcToPoints with this temporary arc and arcResolutionDegrees.
                    // 4. Adding the returned points (excluding the first point, as currentDxfPoint is already added).
                    // Example (conceptual, needs proper implementation):
                    // var (success, center, radius, startAngle, endAngle) = LwPolylineVertex.BulgeToArcParameters(currentVertexInfo.Position, nextVertexInfo.Position, currentVertexInfo.Bulge);
                    // if (success) {
                    //    Arc tempArc = new Arc(center, radius, startAngle, endAngle); // Angles in degrees
                    //    List<Point> arcPoints = ConvertArcToPoints(tempArc, arcResolutionDegrees);
                    //    if (arcPoints.Count > 1) points.AddRange(arcPoints.Skip(1)); // Add arc points, skip first as it's currentDxfPoint
                    // }
                    // For now, the bulge is ignored for trajectory points, only the vertices are added.
                    // This means curved polyline segments will be represented as straight lines between vertices in the trajectory.
                }
            }

            // If the polyline is closed and was defined by more than one distinct vertex,
            // the loop should have added points up to the last vertex. The connection from last to first is implied by IsClosed.
            // If the list of points has the first point effectively repeated at the end, remove it to avoid bad connections.
            if (polyline.IsClosed && points.Count > 1 && Point.Subtract(points.First(), points.Last()).Length < 0.001)
            {
                // points.RemoveAt(points.Count - 1); // No, for trajectory we need the closing point.
                // This logic depends on how the robot consumes points. If it auto-closes, then remove.
                // If it needs explicit close, ensure it's there. Current adds all vertices.
            }
            // If it's a single point polyline (e.g. a DXF POINT entity often comes as LwPolyline with 1 vertex)
            else if (polyline.Vertices.Count == 1 && !points.Any()){
                 points.Add(new Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }

            return points;
        }
    }
}
