using System;
using System.Globalization;
using System.Text;
using static KinematicEngine.RS274NGC;

namespace KinematicEngine
{
    // Enums from canon.h
    public enum CANON_PLANE
    {
        XY = 1, YZ = 2, XZ = 3
    }

    public enum CANON_UNITS
    {
        Undefined = -1, Inches = 1, Mm = 2, Cm = 3
    }

    public enum CANON_MOTION_MODE
    {
        CANON_EXACT_STOP = 1, CANON_EXACT_PATH = 2, CANON_CONTINUOUS = 3
    }

    public enum CANON_SPINDLE_MODE
    {
        CANON_SPINDLE_NORMAL = 1, CANON_SPINDLE_CSS = 2
    }

    public enum CANON_SPEED_FEED_MODE
    {
        CANON_SYNCHED = 1, CANON_INDEPENDENT = 2

    }

    public enum CANON_DIRECTION
    {
        CANON_STOPPED = 1, CANON_CLOCKWISE = 2, CANON_COUNTERCLOCKWISE = 3
    }

    public enum CANON_FEED_REFERENCE
    {
        CANON_WORKPIECE = 1, CANON_XYZ = 2
    }

    public enum CANON_AXIS
    {
        X = 1, Y = 2, Z = 3, A = 4, B = 5, C = 6, U = 7, V = 8
    }

    public enum CANON_SIDE
    {
        CANON_SIDE_RIGHT = 1, CANON_SIDE_LEFT = 2, CANON_SIDE_OFF = 3
    }

    public struct CANON_VECTOR
    {
        public double X, Y, Z, A, B, C, U, V;
    }


    public struct CANON_POSITION
    {
        public double X, Y, Z, A, B, C, U, V;
    }


    public class CANON_TOOL_TABLE
    {
        public int Slot, Id;
        public double Length, Diameter, XOffset, YOffset, FeedTime, FeedDist;
        public string? Comment, ToolImage;
    }




    public class Canon
    {
        private static int lineNumber = 1;
        public static CANON_VECTOR programOrigin;
        static CANON_UNITS lengthUnits = CANON_UNITS.Mm;
        private static CANON_PLANE activePlane = CANON_PLANE.XY;
        static CANON_MOTION_MODE motionMode = CANON_MOTION_MODE.CANON_EXACT_STOP;
        static CANON_SPINDLE_MODE spindleMode = CANON_SPINDLE_MODE.CANON_SPINDLE_NORMAL;

        public static StringBuilder Output = new StringBuilder();
        public static StringBuilder ErrorOutput = new StringBuilder();

        // Host application must assign these
        public static GCodeInterpreter GC = null!;
        public static CCoordMotion CM = null!;

        private static double GetTickSeconds() => Environment.TickCount / 1000.0;

        public CKinematics Kinematics = null!;


        private static void Print(string message)
        {
            Output.AppendFormat("{0,5}", lineNumber++);
            Output.Append(message.TrimEnd('\n'));
            Output.AppendFormat("@{0:F3}\r\n", GetTickSeconds());
        }





        public static void SET_ORIGIN_OFFSETS(double x, double y, double z, double a, double b, double c, double u, double v)
        {
            // emit canonical line
            Print($"SET_ORIGIN_OFFSETS({x:F4}, {y:F4}, {z:F4}, {a:F4}, {b:F4}, {c:F4}, {u:F4}, {v:F4})");

            // guard against mid‐stream changes
            if (CheckIfThreadingInProgress())
                return;

            // save old interpreter state so offsets only apply once
            GC.SaveStateOnceOnly();

            // convert from user units to machine (inches or degrees)
            double xi = GC.UserUnitsToInchesX(x);
            double yi = GC.UserUnitsToInches(y);
            double zi = GC.UserUnitsToInches(z);
            double ai = GC.UserUnitsToInches(a);
            double bi = GC.UserUnitsToInches(b);
            double ci = GC.UserUnitsToInches(c);
            double ui = GC.UserUnitsToInches(u);
            double vi = GC.UserUnitsToInches(v);

            // push into the motion engine
            CM.SetOriginOffsets(xi, yi, zi, ai, bi, ci, ui, vi);


        }


        //Device and Init
        public static void SET_CANON_DEVICE(int device)
        {
            Print($"SET_CANON_DEVICE({device})");
        }


        public static void INIT_CANON()
        {
            Print("INIT_CANON()");
        }


        //Coordinates and units




        public static void USE_LENGTH_UNITS(CANON_UNITS unit)
        {
            string _unit = unit == CANON_UNITS.Inches ? "CANON_UNITS_INCHES"
                        : unit == CANON_UNITS.Mm ? "CANON_UNITS_MM"
                        : unit == CANON_UNITS.Cm ? "CANON_UNITS_CM"
                        : "CANON_UNITS_UNDEFINED";
            Print($"USE_LENGTH_UNITS({_unit})");

        }

        public static void SELECT_PLANE(CANON_PLANE plane)
        {

            string name = plane == CANON_PLANE.XY ? "CANON_PLANE_XY"
                       : plane == CANON_PLANE.YZ ? "CANON_PLANE_YZ"
                       : plane == CANON_PLANE.XZ ? "CANON_PLANE_XZ"
                       : "UNKNOWN";
            Print($"SELECT_PLANE({name})");
        }


        //Traverse and Feeds

        public static void SET_TRAVERSE_RATE(double rate)
        {
            Console.WriteLine($"SET_TRAVERSE_RATE({rate:F4})");
        }

        public static void STRAIGHT_TRAVERSE(double x, double y, double z, double a, double b, double c, double u, double v, bool noCallback, int seq, int id) //11 args
        {
            Console.WriteLine($"STRAIGHT_TRAVERSE({x:F4}, {y:F4}, {z:F4}, {a:F4}, {b:F4}, {c:F4}, {u:F4}, {v:F4})");

            if (CheckIfThreadingInProgress()) return;

            GC.SaveStateOnceOnly();  // save the state here before creating any motion segments

            CM.StraightTraverse(GC.UserUnitsToInchesX(x + _setup.axis_offset_x + _setup.origin_offset_x + _setup.tool_xoffset),
                                GC.UserUnitsToInches(y + _setup.axis_offset_y + _setup.origin_offset_y + _setup.tool_yoffset),
                                GC.UserUnitsToInches(z + _setup.axis_offset_z + _setup.origin_offset_z + _setup.tool_length_offset),
                                GC.UserUnitsToInchesOrDegA(a + _setup.AA_axis_offset + _setup.AA_origin_offset),
                                GC.UserUnitsToInchesOrDegB(b + _setup.BB_axis_offset + _setup.BB_origin_offset),
                                GC.UserUnitsToInchesOrDegC(c + _setup.CC_axis_offset + _setup.CC_origin_offset),
                                GC.UserUnitsToInches(u + _setup.UU_axis_offset + _setup.UU_origin_offset),
                                GC.UserUnitsToInches(v + _setup.VV_axis_offset + _setup.VV_origin_offset),
                 _setup.sequence_number, 0, _setup.feed_rate);

        }
 
        public static void STRAIGHT_TRAVERSE(double x, double y, double z, double a, double b, double c, double u, double v) //8 args
        {
            Console.WriteLine($"STRAIGHT_TRAVERSE({x:F4}, {y:F4}, {z:F4}, {a:F4}, {b:F4}, {c:F4}, {u:F4}, {v:F4})");

            if (CheckIfThreadingInProgress()) return;

            GC.SaveStateOnceOnly();  // save the state here before creating any motion segments

            CM.StraightTraverse(GC.UserUnitsToInchesX(x + _setup.axis_offset_x + _setup.origin_offset_x + _setup.tool_xoffset),
                                GC.UserUnitsToInches(y + _setup.axis_offset_y + _setup.origin_offset_y + _setup.tool_yoffset),
                                GC.UserUnitsToInches(z + _setup.axis_offset_z + _setup.origin_offset_z + _setup.tool_length_offset),
                                GC.UserUnitsToInchesOrDegA(a + _setup.AA_axis_offset + _setup.AA_origin_offset),
                                GC.UserUnitsToInchesOrDegB(b + _setup.BB_axis_offset + _setup.BB_origin_offset),
                                GC.UserUnitsToInchesOrDegC(c + _setup.CC_axis_offset + _setup.CC_origin_offset),
                                GC.UserUnitsToInches(u + _setup.UU_axis_offset + _setup.UU_origin_offset),
                                GC.UserUnitsToInches(v + _setup.VV_axis_offset + _setup.VV_origin_offset),
                 _setup.sequence_number, 0, _setup.feed_rate);

        }
        //public void STRAIGHT_TRAVERSE(double x, double y, double z, int sequence_number, bool noCallback = false)
        // { /* stub */ }


        public static void SET_FEED_RATE(double rate)
        {
            Console.WriteLine($"SET_FEED_RATE({rate:F4})");
        }

        public static void SET_FEED_REFERENCE(CANON_FEED_REFERENCE r)
        {

            r = CANON_FEED_REFERENCE.CANON_WORKPIECE;

            Console.WriteLine($"SET_FEED_REFERENCE({r})");

        }

        public static void SET_MOTION_CONTROL_MODE(CANON_MOTION_MODE m)
        {

            Console.WriteLine($"SET_MOTION_CONTROL_MODE({m})");

        }

        public static void START_SPEED_FEED_SYNCH()
        {
            Console.WriteLine("START_SPEED_FEED_SYNCH()");
        }

        public static void STOP_SPEED_FEED_SYNCH()
        {
            Console.WriteLine("STOP_SPEED_FEED_SYNCH()");
        }

        public static void STRAIGHT_FEED(double x, double y, double z, double a, double b, double c, double u, double v)
        {
            // 1) print line header
            Console.WriteLine($"STRAIGHT_FEED({x:F4}, {y:F4}, {z:F4}, {a:F4}, {b:F4}, {c:F4}, {u:F4}, {v:F4})");

            // 2) threading check (returns true if we should bail)
            if (CheckIfThreadingInProgress())
                return;

            // 3) compute deltas
            double dx = x - programOrigin.X;
            double dy = y - programOrigin.Y;
            double dz = z - programOrigin.Z;
            double da = a - programOrigin.A; // assume you store prior position in programOrigin
            double db = b - programOrigin.B;
            double dc = c - programOrigin.C;
            double du = u - programOrigin.U;
            double dv = v - programOrigin.V;

            // 4) compute pure‐angle vs linear distance
            bool pureAngle;
            double feedDist = CKinematics.FeedRateDistance(dx, dy, dz, da, db, dc, du, dv, out pureAngle);
            // 5) lookup feed rate from GC/_setup (in units per minute), convert
            double rawFeed = GC.CurrentFeedRate; // or however you track it
            double feedRate = pureAngle
                ? rawFeed / 60.0
                : GC.UserUnitsToInches(rawFeed) / 60.0;

            // 6) save state before issuing segments
            GC.SaveStateOnceOnly();

            // 7) call into the motion engine
            CM.StraightFeed(feedRate,
                GC.UserUnitsToInchesX(x  /* axis offset */),
                GC.UserUnitsToInches(y  /* axis offset */),
                GC.UserUnitsToInches(z  /* axis offset */),
                GC.UserUnitsToInchesOrDegA(a  /* offset */),
                GC.UserUnitsToInchesOrDegB(b  /* offset */),
                GC.UserUnitsToInchesOrDegC(c  /* offset */),
                GC.UserUnitsToInches(u  /* + offset */),
                GC.UserUnitsToInches(v  /* + offset */),
                pureAngle ? 1 : 0);

            // 8) update programOrigin for next delta
            programOrigin = new CANON_VECTOR { X = x, Y = y, Z = z /* etc */ };
        }

        public static void ARC_FEED(CANON_FEED_REFERENCE rf, double firstEnd, double secondEnd, double firstAxis, double secondAxis, int rotation, double axisEndPoint, double a, double b, double c, double u, double v)
        {
            Print($"ARC_FEED({firstEnd:F4}, {secondEnd:F4}, {firstAxis:F4}, {secondAxis:F4}, {rotation}, {axisEndPoint:F4})");
            if (CheckIfThreadingInProgress())
                return;

            // compute target XYZ from rf/firstAxis/secondAxis
            double x, y, z;
            if (rf == CANON_FEED_REFERENCE.CANON_WORKPIECE)
            {
                // interpret firstAxis/secondAxis as offsets in current workpiece plane
                CCoordMotion.ResolveArcCartesian(programOrigin, activePlane, firstEnd, secondEnd, firstAxis, secondAxis, rotation, out x, out y, out z);
            }
            else // CanonFeedReference.Xyz
            {
                x = firstEnd; y = secondEnd; z = axisEndPoint;
            }

            // now same as straight‐feed after determining x,y,z,a,b,c,u,v
            STRAIGHT_FEED(x, y, z, a, b, c, u, v);
        }


        // Compensation & Overrides


        public static void SET_CUTTER_RADIUS_COMPENSATION(double radius)
        {
            Print($"SET_CUTTER_RADIUS_COMPENSATION({radius:F4})");
        }

        public static void START_CUTTER_RADIUS_COMPENSATION(CANON_SIDE side)
        {
            Print($"START_CUTTER_RADIUS_COMPENSATION({side})");
        }

        public static void STOP_CUTTER_RADIUS_COMPENSATION()
        {
            Print("STOP_CUTTER_RADIUS_COMPENSATION()");
        }

        public static void ENABLE_FEED_OVERRIDE()
        {
            Print("ENABLE_FEED_OVERRIDE()");
        }

        public static void DISABLE_FEED_OVERRIDE()
        {
            Print("DISABLE_FEED_OVERRIDE()");
        }

        public static void ENABLE_SPEED_OVERRIDE()
        {
            Print("ENABLE_SPEED_OVERRIDE()");
        }

        public static void DISABLE_SPEED_OVERRIDE()
        {
            Print("DISABLE_SPEED_OVERRIDE()");
        }



        public static void FLOOD_ON()
        {
            Print("FLOOD_ON()");
        }

        public static void FLOOD_OFF()
        {
            Print("FLOOD_OFF()");
        }

        public static void MIST_ON()
        {
            Print("MIST_ON()");
        }

        public static void MIST_OFF()
        {
            Print("MIST_OFF()");
        }


        // Spindle & Tool
        public static void SET_SPINDLE_MODE(CANON_SPINDLE_MODE mode)
        {
            Print($"SET_SPINDLE_MODE({mode})");
            GC.SetCSS((int)mode);
        }

        public static void START_SPINDLE_CLOCKWISE()
        {
            Print("START_SPINDLE_CLOCKWISE()");
        }
        public static void START_SPINDLE_COUNTERCLOCKWISE()
        {
            Print("START_SPINDLE_COUNTERCLOCKWISE()");
        }
        public static void SET_SPINDLE_SPEED(double speed)
        {
            Print($"SET_SPINDLE_SPEED({speed:F4})");
        }
        public static void STOP_SPINDLE_TURNING()
        {
            Print("STOP_SPINDLE_TURNING()");
        }
        public static void SPINDLE_RETRACT()
        {
            Print("SPINDLE_RETRACT()");
        }
        public static void ORIENT_SPINDLE(double orientation, CANON_DIRECTION direction)
        {
            Print($"ORIENT_SPINDLE({orientation:F4}, {direction})");
        }
        public static void CHANGE_TOOL(int slot)
        {
            Print($"CHANGE_TOOL({slot})");
        }

        // Axis Controls & Messages
        public static void CLAMP_AXIS(CANON_AXIS ax)
        {
            Print($"CLAMP_AXIS({ax})");
        }

        public static void UNCLAMP_AXIS(CANON_AXIS ax)
        {
            Print($"UNCLAMP_AXIS({ax})");
        }

        public static void MESSAGE(string s)
        {
            Print($"MESSAGE(\"{s}\")");
        }

        public static void PALLET_SHUTTLE()
        {
            Print("PALLET_SHUTTLE()");
        }

        public static void TURN_PROBE_ON()
        {
            Print("TURN_PROBE_ON()");
        }

        public static void TURN_PROBE_OFF()
        {
            Print("TURN_PROBE_OFF()");
        }

        // NURBS
        public static void NURB_KNOT_VECTOR()
        {
            Print("NURB_KNOT_VECTOR()");
        }

        public static void NURB_CONTROL_POINT(int i, double x, double y, double z, double w)
        {
            Print($"NURB_CONTROL_POINT({i}, {x:F4}, {y:F4}, {z:F4}, {w:F4})");
        }

        public static void NURB_FEED(double s0, double s1)
        {
            Print($"NURB_FEED({s0:F4}, {s1:F4})");
        }

        // Program Flow
        public static void OPTIONAL_PROGRAM_STOP()
        {
            Print("OPTIONAL_PROGRAM_STOP()");
        }

        public static void PROGRAM_END(int m)
        {
            Print($"PROGRAM_END({m})");
        }

        public static void PROGRAM_STOP()
        {
            Print("PROGRAM_STOP()");
        }

        // External & Probe
        public static double GET_EXTERNAL_POSITION()
        {

            return 0;
        }
        public static void GET_EXTERNAL_PARAMETER_FILE_NAME(StringBuilder sb, int maxSize)
        {
            Print($"GET_EXTERNAL_PARAMETER_FILE_NAME(maxSize={maxSize})");
            sb.Clear();
        }

        public static double GET_EXTERNAL_PROBE_POSITION_X()
        {
            Print("GET_EXTERNAL_PROBE_POSITION_X()");
            return 0.0;
        }

        public static double GET_EXTERNAL_PROBE_POSITION_Y()
        {
            Print("GET_EXTERNAL_PROBE_POSITION_Y()");
            return 0.0;
        }

        public static double GET_EXTERNAL_PROBE_POSITION_Z()
        {
            Print("GET_EXTERNAL_PROBE_POSITION_Z()");
            return 0.0;
        }

        public static double GET_EXTERNAL_PROBE_POSITION_V()
        {
            Print("GET_EXTERNAL_PROBE_POSITION_V()");
            return 0.0;

        }
        public static int GET_EXTERNAL_QUEUE_EMPTY()
        {
            return 1;
        }

        public static double GET_EXTERNAL_PROBE_VALUE()
        {
            Print("GET_EXTERNAL_PROBE_VALUE()");
            return 0.0;
        }

        public static CANON_UNITS GET_EXTERNAL_LENGTH_UNIT_TYPE()
        {
            return lengthUnits;
        }

        // threading & update functions
        public static void CHECK_INIT_ON_EXE()
        {
            Print("CHECK_INIT_ON_EXE()");
        }

        public static void CHECK_PREVIOUS_STOP()
        {
            Print("CHECK_PREVIOUS_STOP()");
        }

        public static void CheckForBufferedCommand()
        {
            Print("CheckForBufferedCommand()");
        }

        public static void CheckForPassThroughCommand()
        {
            Print("CheckForPassThroughCommand()");
        }

        public static void CheckForUserCallback()
        {
            Print("CheckForUserCallback()");
        }

        public static bool CheckIfThreadingInProgress()
        {
            Print("CheckIfThreadingInProgress()");
            return false;
        }

        public static void HandleThreading()
        {
            Print("HandleThreading()");
        }

        public static void CANON_UPDATE_POSITION()
        {
            Print("CANON_UPDATE_POSITION()");
            return;
        }

        public static bool IS_EXTERNAL_QUEUE_EMPTY()
        {
            Print("IS_EXTERNAL_QUEUE_EMPTY()");
            return true;
        }
        // returns the current a-axis position
        public static double GET_EXTERNAL_POSITION_A()
        {
            return 0.0;
        }

        // returns the current b-axis position
        public static double GET_EXTERNAL_POSITION_B()
        {
            return 0.0;
        }

        // returns the current c-axis position
        public static double GET_EXTERNAL_POSITION_C()
        {
            return 0.0;
        }

        // returns the current u-axis position
        public static double GET_EXTERNAL_POSITION_U()
        {
            return 0.0;
        }

        // returns the current v-axis position
        public static double GET_EXTERNAL_POSITION_V()
        {
            return 0.0;
        }

        // returns the current x-axis position
        public static double GET_EXTERNAL_POSITION_X()
        {
            return 0.0;
        }

        // returns the current y-axis position
        public static double GET_EXTERNAL_POSITION_Y()
        {
            return 0.0;
        }

        // returns the current z-axis position
        public static double GET_EXTERNAL_POSITION_Z()
        {
            return 0.0;
        }
        public static double GET_EXTERNAL_SPEED()
        {
            Print("GET_EXTERNAL_SPEED()");
            return 0.0;
        }

        public static int GET_EXTERNAL_TOOL()
        {
            Print("GET_EXTERNAL_TOOL()");
            return 0;
        }

        public static double GET_EXTERNAL_TOOL_LENGTH_OFFSET()
        {
            Print("GET_EXTERNAL_TOOL_LENGTH_OFFSET()");
            return 0.0;
        }

        public static int GET_EXTERNAL_TOOL_MAX()
        {
            Print("GET_EXTERNAL_TOOL_MAX()");
            return 0;
        }

        public static int GET_EXTERNAL_TOOL_SLOT()
        {
            Print("GET_EXTERNAL_TOOL_SLOT()");
            return 0;
        }

        public static double GET_EXTERNAL_TRAVERSE_RATE()
        {
            Print("GET_EXTERNAL_TRAVERSE_RATE()");
            return 0.0;
        }

        public static CANON_MOTION_MODE GET_EXTERNAL_MOTION_CONTROL_MODE()
        {
            Print($"GET_EXTERNAL_MOTION_CONTROL_MODE({motionMode})");
            return motionMode;
        }

        public static CANON_SPINDLE_MODE GET_EXTERNAL_SPINDLE_MODE()
        {
            Print($"GET_EXTERNAL_MOTION_CONTROL_MODE({spindleMode})");
            return spindleMode;
        }

        public static void PARAMETRIC_2D_CURVE_FEED()
        {
            Print("PARAMETRIC_2D_CURVE_FEED()");
        }

        public static void PARAMETRIC_3D_CURVE_FEED()
        {
            Print("PARAMETRIC_3D_CURVE_FEED()");
        }

        public static int M100(int mcode)
        {
            Print($"M100({mcode})");
            return 0;
        }

                /// <summary>
        /// Pulls slot n out of your external tool‐changer / tool‐offset store.
        /// Must return a fully‐populated CANON_TOOL_TABLE.
        /// If you don’t have any real tooling data yet, just return an “empty” table.
        /// </summary>
        public static CANON_TOOL_TABLE GET_EXTERNAL_TOOL_TABLE(int n)
        {
            // TODO: replace this with your real hardware / DB call
            return new CANON_TOOL_TABLE {
                // the C version zeroes everything but the id, so we’ll do the same:
                Slot      = n,
                Id        = 0,
                Length    = 0.0,
                Diameter  = 0.0,
                XOffset   = 0.0,
                YOffset   = 0.0,
                FeedTime  = 0.0,
                FeedDist  = 0.0,
                Comment   = string.Empty,
                ToolImage = string.Empty
            };
        }

        public static string GET_EXTERNAL_PARAMETER_FILE_NAME() => throw new NotImplementedException();
        public static bool GET_EXTERNAL_FLOOD() => throw new NotImplementedException();
        public static bool GET_EXTERNAL_MIST() => throw new NotImplementedException();
        public static int GET_EXTERNAL_FEEDRATE() => throw new NotImplementedException();
        public static int CutterComp() => throw new NotImplementedException();
        public static int GET_DEFAULT_ARC_TOLERANCE() => throw new NotImplementedException();
        public static int GET_EXTERNAL_PLANE() => throw new NotImplementedException();
        public static int SPIN() => throw new NotImplementedException();
        

    }
}
