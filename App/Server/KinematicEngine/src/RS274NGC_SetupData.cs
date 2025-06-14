using System.Runtime.InteropServices;


namespace KognaServer.Server.KinematicEngine
{

    public partial class RS274NGC
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SetupData
        {
            public int sequence_number;
            // ---- C++: #define RS274NGC_MAX_PARAMETERS 5400 ----
            public const int RS274NGC_MAX_PARAMETERS = 5400;

            // ---- persistent parameters (double parameters[RS274NGC_MAX_PARAMETERS]) ----
            public double[] parameters = new double[RS274NGC_MAX_PARAMETERS];

            // ---- one-line state ----
            public int line_length;
            public char[] linetext = new char[RS274NGC.RS274NGC_TEXT_SIZE];
            public char[] blocktext = new char[RS274NGC.RS274NGC_TEXT_SIZE];
            public CANON_UNITS length_units;
            public bool half_step_mode;

            // ---- logical axis mappings ----
            public int x_axis;
            public int y_axis;
            public int z_axis;
            public int a_axis;
            public int b_axis;
            public int c_axis;
            public int u_axis;
            public int v_axis;

            // ---- parameter-change tracking ----
            public int n_ParamChanges;
            public int[] ParamChanges = new int[MAX_PARAM_CHANGES];

            // ---- last commanded positions (interpreter state) ----
            public double current_x;
            public double current_y;
            public double current_z;
            public double current_a;
            public double current_b;
            public double current_c;
            public double current_u;
            public double current_v;

            // ---- additional NGC state fields ----
            public double UU_current;
            public double VV_current;
            public double AA_current;
            public double BB_current;
            public double CC_current;
            public double XX_current;
            public double YY_current;
            public double ZZ_current;
            public int feed_rate;
            public int flood;
            public int mist;
            public int plane;
            public int selected_tool_slot;
            public int speed;
            public int spindle_turning;
            public int traverse_rate;
            public int arc_radius_tol;
        }
    }
}