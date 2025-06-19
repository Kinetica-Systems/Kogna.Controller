using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Semi.Avalonia.Tokens;


namespace KinematicEngine
{

    public partial class RS274NGC
    {




        public class Block
        {
            public double OLD_X { get; set; }
            public double OLD_Y { get; set; }
            public double OLD_Z { get; set; }
            public int clear_z;
            public int direction;
            public int bottom_z;
            public bool a_flag; public double a_number;
            public bool b_flag; public double b_number;
            public bool c_flag; public double c_number;
            public bool u_flag; public double u_number;
            public bool v_flag; public double v_number;
            public char[] comment = new char[256];
            public int d_number; public bool d_flag;
            public bool f_flag; public double f_number;
            public bool g_flag; public double g_number;
            public int[] g_modes = new int[15];
            public bool h_flag; public int h_number;
            public bool i_flag; public double i_number;
            public bool j_flag; public double j_number;
            public bool k_flag; public double k_number;
            public bool l_flag; public int l_number;
            public int line_number;
            public int motion_to_be;
            public int m_count; public int[] m_modes = new int[11];
            public bool p_flag; public double p_number;
            public bool q_flag; public double q_number;
            public bool r_flag; public double r_number;
            public bool s_flag; public double s_number;
            public bool t_flag; public int t_number;
            public bool x_flag; public double x_number;
            public bool y_flag; public double y_number;
            public bool z_flag; public double z_number;

        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SetupData
        {
            public CCoordMotion CM = null!;
            public static int StackIndex { get; set; } = 0;
            public static string[] Stack { get; } = new string[50];
            public char[] linetext = new char[RS274NGC_TEXT_SIZE];  //interpreter linetext
            public char[] blocktext = new char[RS274NGC_TEXT_SIZE];    //interpreter blocktext
            public int sequence_number { get; set; }
            // ---- C++: #define RS274NGC_MAX_PARAMETERS 5400 ----
            public const int RS274NGC_MAX_PARAMETERS = 5400;
            public const int MAX_SUB_STACK = 10;
            /// <summary>Max rapid accel (mm/s²)</summary>
            public double RapidAccelMax { get; set; } = 500.0;

            /// <summary>Max rapid jerk (mm/s³)</summary>
            public double RapidJerkMax  { get; set; } = 5000.0;

            /// <summary>Micro‐segment time step (s)</summary>
            public double SliceDt       { get; set; } = 0.01;

            // ---- persistent parameters (double parameters[RS274NGC_MAX_PARAMETERS]) ----
            public double[] parameters = null!;

            // ---- one-line state ----
            public int line_length;
            public int line_number;
            public Block? block0;
            public Block? block1;
            public Block? block2;
            public int block_delete;
            public CANON_UNITS length_units;
            public CANON_UNITS length_units_of_origin;
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
            public char[] filename = new char[RS274NGC_TEXT_SIZE];
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
            public double feed_rate;
            public int feed_mode;
            public bool feed_override;
            public bool flood;
            public bool mist;
            public int plane;
            public int selected_tool_slot;
            public int speed;
            public double speed_feed_mode;
            public bool speed_override;
            public int spindle_turning;
            public int traverse_rate;
            public int arc_radius_tol;
            public int origin_index;
            public double origin_offset_x;
            public double origin_offset_y;
            public double origin_offset_z;
            public double axis_offset_x;
            public double axis_offset_y;
            public double axis_offset_z;
            public double AA_axis_offset;
            public double BB_axis_offset;
            public double CC_axis_offset;
            public double UU_axis_offset;
            public double VV_axis_offset;
            public double AA_origin_offset;
            public double BB_origin_offset;
            public double CC_origin_offset;
            public double UU_origin_offset;
            public double VV_origin_offset;
            public double mid_offset_Z;
            public int cutter_comp_side;
            public double cutter_comp_radius;
            public int CompEntryStyle;
            public int retract_mode;      // you referenced this
            public int spindle_state;     // and this
            public static cutter_comp cutter_comp;
            public int motion_mode;
            public probe_flag probe_flag;
            public int distance_mode;

            public StreamReader? file_pointer;
            public int length_offset_index;
            public double program_x;
            public double program_y;
            public double program_z;
            public double pending_comp_x;
            public double pending_comp_y;
            public double pending_comp_z;

            public double tool_length_offset;
            public double tool_xoffset;
            public double tool_yoffset;
            public int tool_zoffset;
            public int tool_zoffset_index;
            public int tool_max;
            public int current_tool_index;
            public int control_mode;
            public int spindle_mode;
            public int spindle_speed;
            public int parameter_occurrence;

            public CANON_TOOL_TABLE[]? tool_table;
            public int[]? active_g_codes;
            public int[]? active_m_codes;
            public int[]? active_settings;
            public int[]? parameter_numbers;
            public int[]? parameter_values;

        }
        public enum KOGNA_TOKEN : int// KMotionLocked Return Codes
        {
            KMOTION_LOCKED = 0,
            KMOTION_IN_USE = 1,
            KMOTION_NOT_CONNECTED = 2
        }
        public enum KOGNA_CHECK_READY : int// KMotion CheckReady Return Codes
        {
            OK=0,
            TIMEOUT=1,
            READY=2,
            ERROR=3,
        }
        public enum IO_TYPE : int
        {
            UNDEFINED,
            DIGITAL_IN,
            DIGITAL_OUT,
            ANALOG_IN,
            ANALOG_OUT
        }
        public enum MCODE_TYPE : int
        {
            M_Action_None = 0,
            M_Action_Setbit = 1,
            M_Action_SetTwoBits = 2,
            M_Action_DAC = 3,
            M_Action_Program = 4,
            M_Action_Program_wait = 5,		
            M_Action_Program_wait_sync = 6,	
            M_Action_Program_PC = 7,
            M_Action_Callback = 8,
            M_Action_Waitbit = 9,
        }

    public enum PREV_STOP_TYPE : int
    {
        Prev_Stopped_None = 0,
        Prev_Stopped_Indep = 1,
        Prev_Stopped_Coord = 2,
        Prev_Stopped_Coord_Finished = 3,
    }
        public enum cutter_comp
        {
            OFF = 0, LEFT = 1, RIGHT = 2
        }
        public enum probe_flag
        {
            OFF = 0, ON = 1
        }
        public enum DISTANCE_MODE
        {
            MODE_ABSOLUTE = 0, MODE_INCREMENTAL = 1
        }
        public enum RS274NGC_FEED_MODE
        {
            PER_MINUTE = 0, INVERSE_TIME = 1, PER_REV = 3,
        }

        public enum RS274NGC_COOLANT
        {
            DISABLED = -1, OFF = 0, MIST_ON = 7, FLOOD_ON = 8, MIST_OFF = 9,
        }

        public enum RS274NGC_FLOOD
        {
            OFF = 0, ON = 1,
        }

        public enum RS274NGC_MIST
        {
            OFF = 0, ON = 1,
        }

        public enum RS274NGC_MOTION_MODE
        {
            G_80 = 0, G_81 = 1
        }

        public enum SPINDLE_STATE
        {
            STOPPED = 0, CW = 1, CCW = 2
        }

        public enum RETRACT_MODE 
        {
            R_PLANE = 0,
            OLD_Z = 1
        }

        public const int G_0 = 0;
        public const int G_1 = 10;
        public const int G_2 = 20;
        public const int G_3 = 30;
        public const int G_4 = 40;
        public const int G_10 = 100;
        public const int G_17 = 170;
        public const int G_18 = 180;
        public const int G_19 = 190;
        public const int G_20 = 200;
        public const int G_21 = 210;
        public const int G_28 = 280;
        public const int G_30 = 300;
        public const int G_32 = 320;
        public const int G_33 = 330;
        public const int G_38_2 = 382;
        public const int G_40 = 400;
        public const int G_41 = 410;
        public const int G_42 = 420;
        public const int G_43 = 430;
        public const int G_43_4 = 434;
        public const int G_49 = 490;
        public const int G_52 = 520;
        public const int G_53 = 530;
        public const int G_54 = 540;
        public const int G_55 = 550;
        public const int G_56 = 560;
        public const int G_57 = 570;
        public const int G_58 = 580;
        public const int G_59 = 590;
        public const int G_59_1 = 591;
        public const int G_59_2 = 592;
        public const int G_59_3 = 593;
        public const int G_61 = 610;
        public const int G_61_1 = 611;
        public const int G_64 = 640;
        public const int G_76 = 760;
        public const int G_80 = 800;
        public const int G_81 = 810;
        public const int G_82 = 820;
        public const int G_83 = 830;
        public const int G_84 = 840;
        public const int G_85 = 850;
        public const int G_86 = 860;
        public const int G_87 = 870;
        public const int G_88 = 880;
        public const int G_89 = 890;
        public const int G_90 = 900;
        public const int G_91 = 910;
        public const int G_92 = 920;
        public const int G_92_1 = 921;
        public const int G_92_2 = 922;
        public const int G_92_3 = 923;
        public const int G_93 = 930;
        public const int G_94 = 940;
        public const int G_95 = 950;
        public const int G_96 = 960;
        public const int G_97 = 970;
        public const int G_98 = 980;
        public const int G_99 = 990;



        public static int ERM(int errorCode, string name)
        {


            SetupData.StackIndex = 0;
            if (SetupData.StackIndex < 50)
            {
                SetupData.Stack[SetupData.StackIndex++] = name;
                SetupData.Stack[SetupData.StackIndex] = string.Empty;
            }
            return errorCode;
        }

        public static int ERP(int errorCode, string name)
        {
            if (SetupData.StackIndex < 49)
            {
                SetupData.Stack[SetupData.StackIndex++] = name;
                SetupData.Stack[SetupData.StackIndex] = string.Empty;
            }
            return errorCode;
        }

        public static int CHK(bool bad, int errorCode, string name)
        {
            if (bad)
            {
                SetupData.StackIndex = 0;
                if (SetupData.StackIndex < 50)
                {
                    SetupData.Stack[SetupData.StackIndex++] = name;
                    SetupData.Stack[SetupData.StackIndex] = string.Empty;
                }
                return errorCode;
            }
            return 0; // Equivalent to "else do nothing"
        }

        public static int CHP(Func<int> tryThis, string name)
        {
            int status = tryThis();
            const int RS274NGC_OK = 0;

            if (status != RS274NGC_OK)
            {
                if (SetupData.StackIndex < 49)
                {
                    SetupData.Stack[SetupData.StackIndex++] = name;
                    SetupData.Stack[SetupData.StackIndex] = string.Empty;
                }
            }
            return status;
        }

        private const int LEFT_BRACKET = 41;
        private const int RIGHT_BRACKET = 93;
        private const int PLUS = 2;
        private const int MINUS = 3;
        private const int DIVIDED_BY = 4;
        private const int TIMES = 5;
        private const int POWER = 6;
        private const int AND2 = 12;
        private const int NON_EXCLUSIVE_OR = 13;
        private const int EXCLUSIVE_OR = 14;
        private const int LOGICAL_LT = 15;
        private const int LOGICAL_GT = 16;
        private const int LOGICAL_EQ = 17;
        private const int LOGICAL_NE = 18;
        private const int LOGICAL_LE = 19;
        private const int LOGICAL_GE = 20;
        private const int MODULO = 23;
        private const int UNEGATIVE = 24;  // (unary minus)
        private const int UNARY_PLUS = 25;  // if needed
        private const int ATAN = 26;  // for G76 etc

        private static int Precedence(int op)
        {
            return op switch
            {
                PLUS or MINUS => 1,
                TIMES or DIVIDED_BY => 2,
                POWER => 3,
                // logical operators maybe 0 or  -1 if you don’t support them yet
                _ => 0
            };
        }

        /// <summary>Executes a binary operator on two doubles.</summary>
        private static double ExecuteBinary(double op, double left, double right)
        {
            return op switch
            {
                PLUS => left + right,
                MINUS => left - right,
                TIMES => left * right,
                DIVIDED_BY => left / right,
                POWER => Math.Pow(left, right),
                // …other cases for AND2, OR, etc., if you need them…
                _ => throw new InvalidOperationException($"Unknown op {op}")
            };
        }

        private static int ReadOperationUnary(string expr, ref int idx, out double op)
        {
            op = UNEGATIVE;   // default unary‐minus
            if (idx < expr.Length && expr[idx] == '-')
            {
                op = UNEGATIVE;
                idx++;
            }
            else if (idx < expr.Length && expr[idx] == '+')
            {
                op = UNARY_PLUS;
                idx++;
            }
            return RS274NGC_OK;
        }

        private static int ExecuteUnary(double op, int operand)
        {
            return op switch
            {
                UNEGATIVE   => -operand,
                UNARY_PLUS  => +operand,
                _           => operand
            };
        }
    }
    
    
}