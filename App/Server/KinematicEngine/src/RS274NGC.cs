using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System.Collections.Generic;
using Avalonia.Markup.Xaml.MarkupExtensions;
using AvaloniaEdit.Editing;
using Semi.Avalonia.Tokens;

namespace KinematicEngine
{
    public partial class RS274NGC
    {

        public const string RS274NGC_PARAMETER_FILE_NAME_DEFAULT = "rs274ngc.var";    //:contentReference[oaicite:0]{index=0}  
        public const string RS274NGC_PARAMETER_FILE_BACKUP_SUFFIX = ".bak";          //  :contentReference[oaicite:1]{index=1}  
        public const bool OFF = false;            //    :contentReference[oaicite:2]{index=2}  
        public const bool ON = true;            //    :contentReference[oaicite:3]{index=3}  
        public const int UNITS_PER_MINUTE = 0;              //  :contentReference[oaicite:4]{index=4}  
        public const int INVERSE_TIME = 1;            //    :contentReference[oaicite:5]{index=5}  
        public const int UNITS_PER_REV = 2;            //    :contentReference[oaicite:6]{index=6}  
        public const int EMC_COMP_ENTRY_STYLE = 0;             //   :contentReference[oaicite:7]{index=7}  
        public const int FANUC_COMP_ENTRY_STYLE = 1;            //    :contentReference[oaicite:8]{index=8}
        private const int M_COOLANT_ON = 7;
        private const int M_COOLANT_OFF = 9;
        private const int M_MIST_ON = 8;
        private const int M_FLOOD_ON = 7;
        private const int M_FLOOD_OFF = 9;
        private const int M_M100 = 100;
        private const int EXIT_CODE = RS274NGC_EXIT;
        public const int MAX_EMS = 8;
        private const int MaxGComment = 256;  // <-- use the exact value from your header

        // --- Limits from rs274ngc.h --- :contentReference[oaicite:1]{index=1}
        private const int RS274NGC_TEXT_SIZE = 256;
        private const int RS274NGC_MAX_PARAMETERS = 5400;   // actual value from header
        private const int MAX_PARAM_CHANGES = 50;
        private const int MAX_FILENAME_SIZE = 100;
        private const int RS274NGC_ACTIVE_G_CODES = 12;
        private const int RS274NGC_ACTIVE_M_CODES = 7;
        private const int RS274NGC_ACTIVE_SETTINGS = 3;
        private const int CANON_TOOL_MAX = 100;    // placeholder
        private const double SIGMA = 1e-6; //chord-error tolerance


        // --- “Required” parameters list for restore/save --- :contentReference[oaicite:2]{index=2}
        private static readonly int[] requiredParameters = new int[]
        {
            5161,5162,5163,
            5164,5165,5166,
            5181,5182,5183,
            5184,5185,5186,
            5211,5212,5213,
            5214,5215,5216,
            5220,
            5221,5222,5223,
            5224,5225,5226,
            5241,5242,5243,
            5244,5245,5246,
            5261,5262,5263,
            5264,5265,5266,
            5281,5282,5283,
            5284,5285,5286,
            5301,5302,5303,
            5304,5305,5306,
            5321,5322,5323,
            5324,5325,5326,
            5341,5342,5343,
            5344,5345,5346,
            5361,5362,5363,
            5364,5365,5366,
            5381,5382,5383,
            5384,5385,5386,
            RS274NGC_MAX_PARAMETERS
        };


        // --- Internal ring‐buffer for save_parameters_changed() ---
        private static int _nActualParametersToSave = 0;
        private static int[] _actualParametersToSave = new int[RS274NGC_MAX_PARAMETERS];
        private static double[] _actualParameterValues = new double[RS274NGC_MAX_PARAMETERS];

        // --- interfaces?
        private static readonly int[] m_modal_group = Enumerable.Repeat(0, 120).ToArray();
        public static SetupData _setup;

        /// <summary>
        /// Initialize interpreter. :contentReference[oaicite:4]{index=4}
        /// </summary>
        public static int Init()
        {
            _setup = new SetupData();
            _setup.CM = new CCoordMotion();
            // call into your hardware interface
            Canon.INIT_CANON();
            SetupData.StackIndex = 0;
            _setup.length_units = Canon.GET_EXTERNAL_LENGTH_UNIT_TYPE();
            Canon.USE_LENGTH_UNITS(_setup.length_units);

            var fn = Canon.GET_EXTERNAL_PARAMETER_FILE_NAME();
            if (string.IsNullOrEmpty(fn))
                fn = RS274NGC_PARAMETER_FILE_NAME_DEFAULT;
            var status = RestoreParameters(fn);
            if (status != RS274NGC_OK) return status;

            var pars = _setup.parameters;
            _setup.origin_index = (int)(pars[5220] + 0.0001);
            if (_setup.origin_index < 1 || _setup.origin_index > 9)
                return NCE_COORDINATE_SYSTEM_INDEX_PARAMETER_5220_OUT_OF_RANGE;

            int k = 5200 + _setup.origin_index * 20;
            Canon.SET_ORIGIN_OFFSETS(pars[k + 1] + pars[5211], pars[k + 2] + pars[5212], pars[k + 3] + pars[5213],
                pars[k + 4] + pars[5214], pars[k + 5] + pars[5215], pars[k + 6] + pars[5216],
                pars[k + 7] + pars[5217], pars[k + 8] + pars[5218]);

            Canon.SET_FEED_REFERENCE(CANON_FEED_REFERENCE.CANON_XYZ);

            _setup.AA_axis_offset = pars[5214];
            _setup.AA_origin_offset = pars[k + 4];
            _setup.axis_offset_x = pars[5211];
            _setup.axis_offset_y = pars[5212];
            _setup.axis_offset_z = pars[5213];
            _setup.BB_axis_offset = pars[5215];
            _setup.BB_origin_offset = pars[k + 5];
            _setup.blocktext[0] = '\0';
            _setup.CC_axis_offset = pars[5216];
            _setup.CC_origin_offset = pars[k + 6];
            _setup.UU_axis_offset = pars[5217];
            _setup.UU_origin_offset = pars[k + 7];
            _setup.VV_axis_offset = pars[5218];
            _setup.VV_origin_offset = pars[k + 8];
            _setup.cutter_comp_side = 0;
            _setup.CompEntryStyle = EMC_COMP_ENTRY_STYLE;
            _setup.distance_mode = (int)RS274NGC_DISTANCE_MODE.MODE_ABSOLUTE;
            _setup.feed_mode = UNITS_PER_MINUTE;
            _setup.feed_override = true;
            _setup.filename[0] = '\0';
            _setup.file_pointer = null;
            _setup.length_offset_index = -1;
            _setup.line_length = 0;
            _setup.linetext[0] = '\0';
            _setup.motion_mode = G_80;
            _setup.origin_offset_x = pars[k + 1];
            _setup.origin_offset_y = pars[k + 2];
            _setup.origin_offset_z = pars[k + 3];
            _setup.probe_flag = 0;
            _setup.program_x = 0;	/* for cutter comp */
            _setup.program_y = 0;	/* for cutter comp */
            _setup.pending_comp_x = 0;	/* for fanuc cutter comp */
            _setup.pending_comp_y = 0;	/* for fanuc cutter comp */
            _setup.sequence_number = 0;	/* DOES THIS NEED TO BE AT TOP? */
            _setup.speed_feed_mode = (double)CANON_SPEED_FEED_MODE.CANON_SYNCHED;
            _setup.speed_override = true;
            _setup.tool_length_offset = 0.0;
            _setup.tool_xoffset = 0.0;
            _setup.tool_yoffset = 0.0;
            _setup.current_tool_index = 1;


            WriteGCodes((Block)null!, _setup);
            WriteMCodes((Block)null!, _setup);
            WriteSettings(_setup);

            Synch();

            LoadToolTable();
            Reset();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Execute one line or block (MDI or previously read). :contentReference[oaicite:5]{index=5}
        /// </summary>
        public static int Execute(string command = null!)
        {
            int status = CHECK_INIT_ON_EXEC();
            if (status != RS274NGC_OK) return status;

            if (!string.IsNullOrEmpty(command))
                status = Read(command);

            // copy any parameter settings into parameters[]
            for (int i = 0; i < _setup.parameter_occurrence; i++)
                _setup.parameters[_setup.parameter_numbers[i]] = _setup.parameter_values[i];

            if (_setup.line_length != 0)
            {
                status = ExecuteBlock(_setup.block1, _setup);
                WriteGCodes(_setup.block1, _setup);
                WriteMCodes(_setup.block1, _setup);
                WriteSettings(_setup);
                if (status != RS274NGC_OK &&
                    status != RS274NGC_EXECUTE_FINISH &&
                    status != RS274NGC_EXIT)
                    return status;
            }
            else
            {
                status = RS274NGC_OK;
            }
            return status;
        }

        /// <summary>
        /// Exit interpreter, save parameters. :contentReference[oaicite:6]{index=6}
        /// </summary>
        public static int Exit()
        {
            var fn = Canon.GET_EXTERNAL_PARAMETER_FILE_NAME();
            if (string.IsNullOrEmpty(fn))
                fn = RS274NGC_PARAMETER_FILE_NAME_DEFAULT;
            SaveParameters(fn);
            Reset();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Reset line‐interpretation state. :contentReference[oaicite:7]{index=7}
        /// </summary>
        public static int Reset()
        {
            _setup.linetext[0] = '\0';
            _setup.blocktext[0] = '\0';
            _setup.line_length = 0;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Restore parameters from disk, enforcing _requiredParameters. :contentReference[oaicite:8]{index=8}
        /// </summary>
        public static int RestoreParameters(string filename)
        {
            double[] pars = _setup.parameters;
            Array.Clear(pars, 0, pars.Length);

            if (File.Exists(filename))
                return NCE_UNABLE_TO_OPEN_FILE;

            using var reader = new StreamReader(filename);
            int requiredIdx = 0;

            int[] _requiredParameters = new int[requiredIdx++];

            int nextRequired = _requiredParameters[requiredIdx++];


            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                if (!int.TryParse(parts[0], out var variable) ||
                    !double.TryParse(parts[1], out var value))
                    continue;

                if (variable <= 0 || variable >= RS274NGC_MAX_PARAMETERS)
                    return NCE_PARAMETER_NUMBER_OUT_OF_RANGE;

                // fast‐forward to the variable
                while (variable > nextRequired)
                {
                    if (nextRequired == _requiredParameters[^1])
                        return NCE_REQUIRED_PARAMETER_MISSING;
                    nextRequired = _requiredParameters[requiredIdx++];
                }
                if (variable < nextRequired)
                    return NCE_PARAMETER_FILE_OUT_OF_ORDER;

                pars[variable] = value;
                _actualParametersToSave[_nActualParametersToSave] = variable;
                _actualParameterValues[_nActualParametersToSave++] = value;

                nextRequired = (requiredIdx < _requiredParameters.Length)
                    ? _requiredParameters[requiredIdx++]
                    : RS274NGC_MAX_PARAMETERS;
            }

            if (_requiredParameters[^1] != RS274NGC_MAX_PARAMETERS)
                return NCE_REQUIRED_PARAMETER_MISSING;

            // force 5220 ≥ 1
            if (pars[5220] < 1.0) pars[5220] = 1.0;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Save parameters back to disk (renaming original to .bak). :contentReference[oaicite:9]{index=9}
        /// </summary>
        public static int SaveParameters(string filename)
        {
            string backup = filename + ".bak";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(filename, backup);

            // reopen backup for reading
            using var reader = new StreamReader(backup);
            using var writer = new StreamWriter(filename, false);

            int requiredIdx = 0;
            int[] _requiredParameters = new int[requiredIdx++];
            int nextRequired = _requiredParameters[requiredIdx++];

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null) break;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;

                if (!int.TryParse(parts[0], out var variable))
                    continue;

                if (variable <= 0 || variable >= RS274NGC_MAX_PARAMETERS)
                    return NCE_PARAMETER_NUMBER_OUT_OF_RANGE;

                while (variable > nextRequired)
                {
                    if (nextRequired == _requiredParameters[^1])
                        break;
                    nextRequired = _requiredParameters[requiredIdx++];
                }

                writer.WriteLine($"{variable}\t{_setup.parameters[variable]}");
            }

            // ensure any missing required parameters get written
            foreach (var req in _requiredParameters)
            {
                if (!_actualParametersToSave[.._nActualParametersToSave].Contains(req))
                    writer.WriteLine($"{req}\t{_setup.parameters[req]}");
            }

            return RS274NGC_OK;
        }

        /// <summary>
        /// Has any parameter changed since last restore? :contentReference[oaicite:10]{index=10}
        /// </summary>
        public static bool SaveParametersChanged()
            => _nActualParametersToSave > 0;

        /// <summary>
        /// Synchronize internal state with Canon controller. :contentReference[oaicite:11]{index=11}
        /// </summary>
        private static int Synch()
        {
            _setup.control_mode = (int)CANON_SPINDLE_MODE.CANON_SPINDLE_NORMAL;
            _setup.spindle_mode = (int)Canon.GET_EXTERNAL_SPINDLE_MODE();
            _setup.AA_current = Canon.GET_EXTERNAL_POSITION_A();
            _setup.BB_current = Canon.GET_EXTERNAL_POSITION_B();
            _setup.CC_current = Canon.GET_EXTERNAL_POSITION_C();
            _setup.UU_current = Canon.GET_EXTERNAL_POSITION_U();
            _setup.VV_current = Canon.GET_EXTERNAL_POSITION_V();
            _setup.feed_rate = Canon.GET_EXTERNAL_FEEDRATE();
            _setup.flood = Canon.GET_EXTERNAL_FLOOD() ? true : false;
            _setup.length_units = Canon.GET_EXTERNAL_LENGTH_UNIT_TYPE();
            _setup.mist = Canon.GET_EXTERNAL_MIST() ? true : false;
            _setup.plane = Canon.GET_EXTERNAL_PLANE();
            _setup.selected_tool_slot = Canon.GET_EXTERNAL_TOOL_SLOT();
            _setup.speed = (int)Canon.GET_EXTERNAL_SPEED();
            _setup.spindle_turning = Canon.SPIN();
            _setup.tool_max = Canon.GET_EXTERNAL_TOOL_MAX();
            _setup.traverse_rate = Canon.GET_EXTERNAL_TOOL_SLOT();
            _setup.arc_radius_tol = Canon.GET_DEFAULT_ARC_TOLERANCE();

            LoadToolTable();
            return RS274NGC_OK;
        }

        /// <summary>
        /// Load the entire tool table from external world. :contentReference[oaicite:12]{index=12}
        /// </summary>
        public static int LoadToolTable()
        {
            if (_setup.tool_max > CANON_TOOL_MAX)
                return NCE_TOOL_MAX_TOO_LARGE;

            for (int n = 0; n <= _setup.tool_max; n++)
                _setup.tool_table[n] = Canon.GET_EXTERNAL_TOOL_TABLE(n);

            for (int n = _setup.tool_max + 1; n <= CANON_TOOL_MAX; n++)
                _setup.tool_table[n] = new CANON_TOOL_TABLE();

            return RS274NGC_OK;
        }

        /// <summary>
        /// Open an NC‐code file (with '%' handling). :contentReference[oaicite:13]{index=13}
        /// </summary>
        public static int Open(string fileName)
        {
            if (_setup.file_pointer != null) return NCE_A_FILE_IS_ALREADY_OPEN;
            if (fileName.Length >= RS274NGC_TEXT_SIZE) return NCE_COMMAND_TOO_LONG;

            try
            {
                _setup.file_pointer = new StreamReader(fileName);
            }
            catch
            {
                return NCE_UNABLE_TO_OPEN_FILE;
            }

            int result = SkipPercent();
            if (result != RS274NGC_OK) return result;


            Reset();

            return RS274NGC_OK;
        }

        private static int SkipPercent()
        {
            string? line;
            bool seenFirstPercent = false;

            while ((line = _setup.file_pointer.ReadLine()) != null)
            {
                if (line.Trim() == "%")
                {
                    if (!seenFirstPercent)
                    {
                        seenFirstPercent = true;
                        _setup.sequence_number = 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (!seenFirstPercent)
                _setup.file_pointer.BaseStream.Seek(0, SeekOrigin.Begin);

            return RS274NGC_OK;
        }

        /// <summary>
        /// Read next NC line or MDI text. :contentReference[oaicite:14]{index=14}
        /// </summary>
        public static int Read(string? mdi = null)
        {
            if (mdi == null && _setup.file_pointer == null)
                return NCE_FILE_NOT_OPEN;

            string? text = mdi ?? _setup.file_pointer.ReadLine();
            if (text == null) return RS274NGC_ENDFILE;

            _setup.linetext = text.ToCharArray();
            _setup.line_length = _setup.linetext.Length;
            if (_setup.line_length > 0)
            {
                int st = ParseLine(text, out Block b);
                if (st != RS274NGC_OK) return st;
                _setup.block1 = b;
                return RS274NGC_OK;
            }
            return 0;
        }

        // …and similarly port the “getter” APIs:

        public static void ActiveGCodes(int[] codes)
        {
            Array.Copy(_setup.active_g_codes, codes, RS274NGC_ACTIVE_G_CODES);
        }

        public static void ActiveMCodes(int[] codes)
        {
            Array.Copy(_setup.active_m_codes, codes, RS274NGC_ACTIVE_M_CODES);
        }

        public static void ActiveSettings(double[] settings)
        {
            Array.Copy(_setup.active_settings, settings, RS274NGC_ACTIVE_SETTINGS);
        }

        public static void ErrorText(int errorCode, StringBuilder errorText, int maxSize)
        {
            if (errorCode >= 0 && errorCode < Messages.Count)
            {
                var msg = Get(errorCode);
                if (msg.Length < maxSize)
                {
                    errorText.Clear();
                    errorText.Append(msg);
                }
            }
            else
            {
                errorText.Clear();
            }
        }

        public static void FileName(StringBuilder fileName, int maxSize)
        {
            var name = _setup.filename;
            if (name.Length < maxSize)
            {
                fileName.Clear();
                fileName.Append(name);
            }
            else
            {
                fileName.Clear();
            }
        }
        public static int LineLength() => _setup.line_length;

        public static void LineText(StringBuilder lineText, int maxSize)
        {
            var txt = new string(_setup.linetext).TrimEnd('\0');
            if (txt.Length >= maxSize) txt = txt.Substring(0, maxSize - 1);
            lineText.Clear();
            lineText.Append(txt);
        }

        public static int SequenceNumber() => _setup.sequence_number;

        public static void stack_name(int index, StringBuilder functionName, int maxSize)
        {
            string name = string.Empty;
            if (SetupData.Stack != null && index >= 0 && index < SetupData.Stack.Length)
                name = SetupData.Stack[index] ?? string.Empty;
            if (name.Length < maxSize)
            {
                functionName.Clear();
                functionName.Append(name);
            }
            else
            {
                functionName.Clear();
            }
        }


        public static int IniLoad(string filename)
        {
            // No C++ body was provided; stubbed for the moment. :contentReference[oaicite:15]{index=15}
            throw new NotImplementedException();
        }

        // Convenience inlines from rs274ngc.h
        public static int Line() => SequenceNumber();
        public static string Command()
        {
            var sb = new StringBuilder(100);
            LineText(sb, 100);
            return sb.ToString();
        }


        /// <summary>
        /// Mirrors the C struct block_struct. :contentReference[oaicite:1]{index=1}
        /// </summary>


        /// <summary>
        /// Reset every field in a Block to its “not present” sentinel. :contentReference[oaicite:2]{index=2}
        /// </summary>
        private static int InitBlock(Block b)
        {
            b.a_flag = b.b_flag = b.c_flag = b.u_flag = b.v_flag = false;
            b.comment[0] = '\0';
            b.d_number = -1; b.d_flag = false;
            b.f_number = -1.0;
            for (int i = 0; i < 15; i++) b.g_modes[i] = -1;
            b.h_number = -1;
            b.i_flag = b.j_flag = b.k_flag = false;
            b.l_flag = false; b.l_number = -1;
            b.line_number = -1;
            b.motion_to_be = -1;
            b.m_count = 0;
            for (int i = 0; i < 11; i++) b.m_modes[i] = -1;
            b.p_flag = b.q_flag = b.r_flag = false;
            b.p_number = b.q_number = b.r_number = -1.0;
            b.s_number = -1.0;
            b.t_number = -1;
            b.x_flag = b.y_flag = b.z_flag = false;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Top‐level line parser. Calls init_block, read_items, enhance_block, check_items. :contentReference[oaicite:3]{index=3}</summary>
        public static int ParseLine(string line, out Block block)
        {
            block = new Block();
            int status = InitBlock(block);
            if (status != RS274NGC_OK) return status;

            // Read every word (G, M, X, Y, Z, etc.)
            // This loops over the line buffer with read_* functions:
            //   read_real_value, read_g, read_m, read_parameter_setting, …
            // e.g. CHK(read_items(block, line, ref counter, _setup.parameters));
            status = ReadItems(block, line, _setup.parameters);
            if (status != RS274NGC_OK) return status;

            // Apply canned‐cycle / motion‐to‐be logic
            status = EnhanceBlock(block, _setup);
            if (status != RS274NGC_OK) return status;

            // Validate word‐combinations (I‐J‐K only with arcs, no duplicate axes, etc.)
            status = CheckItems(block, _setup);
            return status;
        }

        /// <summary>
        /// Execute one block of parsed instructions in the correct order:
        /// comment → feed-mode → feed-rate → S → T → M → G → stop. :contentReference[oaicite:4]{index=4}</summary>
        public static int ExecuteBlock(Block block, SetupData s)
        {
            int status;
            if (block.comment[0] != '\0')
            {
                CHP(() => ConvertComment(new string(block.comment)), nameof(ConvertComment));
            }

            // feed mode (G-code 5)
            if (block.g_modes[5] != -1)
            {
                CHP(() => ConvertFeedMode(block.g_modes[5], s), nameof(ConvertFeedMode));
            }

            // feed rate (F-word), unless inverse-time mode
            if (block.f_number > -1.0 && s.feed_mode != (int)RS274NGC_FEED_MODE.INVERSE_TIME)
            {
                CHP(() => ConvertFeedRate(block, s), nameof(ConvertFeedRate));
            }

            // spindle mode (G96/G97)
            if (block.g_modes[14] != -1)
            {
                CHP(() => ConvertSpindleMode(block.g_modes[14], s), nameof(ConvertSpindleMode));
            }

            // S-speed (S-word)
            if (block.s_number > -1.0)
            {
                CHP(() => ConvertSpeed(block, s), nameof(ConvertSpeed));
            }

            // T-tool select
            if (block.t_number != -1)
            {
                CHP(() => ConvertToolSelect(block, s), nameof(ConvertToolSelect));
            }

            // plain M- and G-codes
            CHP(() => ConvertM(block, s), nameof(ConvertM));
            CHP(() => ConvertG(block, s), nameof(ConvertG));

            // special M-codes (e.g. M0/M30)
            if (block.m_modes[4] != -1)
            {
                status = ConvertStop(block, s);
                if (status == RS274NGC_EXIT)
                    return RS274NGC_EXIT;
                else if (status != RS274NGC_OK)
                    ERM(status, nameof(ConvertStop));
            }

            // final return: if probe_flag is ON, finish; otherwise just OK
            return ((int)s.probe_flag == 1) ? RS274NGC_EXECUTE_FINISH : RS274NGC_OK;
        }

        /// <summary>
        /// Write out the active G-codes into the setup snapshot. :contentReference[oaicite:5]{index=5}</summary>
        public static int WriteGCodes(Block block, SetupData s)
        {
            var g = s.active_g_codes;
            var compSide = (cutter_comp)s.cutter_comp_side;
            var units = (CANON_UNITS)s.length_units;
            g[0] = s.sequence_number;
            g[1] = s.motion_mode;
            g[2] = block == null ? -1 : block.g_modes[0];
            g[3] = s.plane == (int)CANON_PLANE.XY ? G_17 : s.plane == (int)CANON_PLANE.XZ ? G_18 : G_19;
            g[4] = compSide switch
            {
                cutter_comp.LEFT => G_41,
                cutter_comp.RIGHT => G_42,
                _ => G_40
            };
            g[5] = units == CANON_UNITS.Inches ? G_20 : G_21;
            g[6] = s.distance_mode == (int)RS274NGC_DISTANCE_MODE.MODE_ABSOLUTE ? G_90 : G_91;
            g[7] = s.feed_mode == (int)RS274NGC_FEED_MODE.INVERSE_TIME ? G_93 :
                    s.feed_mode == (int)RS274NGC_FEED_MODE.PER_MINUTE ? G_94 : G_95;
            g[8] = s.origin_index < 7 ? 530 + 10 * s.origin_index : 584 + s.origin_index;
            g[9] = s.tool_length_offset == 0 && s.tool_xoffset == 0 && s.tool_yoffset == 0 ? G_49 : G_43;
            g[10] = s.retract_mode == block.OLD_Z ? G_98 : G_99;
            g[11] = s.motion_mode == (int)CANON_MOTION_MODE.CANON_CONTINUOUS ? G_64 : G_61;
            g[12] = s.spindle_mode == (int)CANON_SPINDLE_MODE.CANON_SPINDLE_NORMAL ? G_97 : G_96;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Write out the active M-codes into the setup snapshot. :contentReference[oaicite:6]{index=6}</summary>
        public static int WriteMCodes(Block block, SetupData s)
        {
            var m = s.active_m_codes;
            m[0] = s.sequence_number;
            m[1] = block == null ? -1 : block.m_modes[4];
            m[2] = s.spindle_turning == (int)SPINDLE_STATE.STOPPED ? 5 : s.spindle_turning == (int)SPINDLE_STATE.CW ? 3 : 4;
            m[3] = block == null ? -1 : block.m_modes[6];
            m[4] = s.mist ? 7 : s.flood ? -1 : 9;

            m[5] = s.flood ? 8 : -1;
            m[6] = s.feed_override ? 48 : 49;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Write out feed, speed, and sequence into active_settings. :contentReference[oaicite:7]{index=7}</summary>
        public static int WriteSettings(SetupData s)
        {
            var a = s.active_settings;
            a[0] = s.sequence_number;
            a[1] = (int)s.feed_rate;
            a[2] = s.speed;
            return RS274NGC_OK;
        }


        private static int ReadItems(Block block, string line, double[] parameters)
        {
            int length = line.Length;
            int counter = 0;

            // Skip leading "/" if block‐delete is on
            if (counter < length && line[counter] == '/')
            {
                counter++;
                if (_setup.block_delete != 0)
                    length = 1;
            }

            // Optional N (or old "O") line number
            if (counter < length && (line[counter] == 'n' || line[counter] == 'o'))
            {
                int status = ReadLineNumber(line, ref counter, block);
                if (status != RS274NGC_OK) return status;
            }

            // Read remaining items
            while (counter < length)
            {
                int status = ReadOneItem(line, ref counter, block, parameters);
                if (status != RS274NGC_OK) return status;
            }

            return RS274NGC_OK;
        }
        private static int ReadOneItem(string line, ref int counter, Block block, double[] parameters)
        {
            char c = line[counter];
            int status;

            switch (c)
            {
                case 'x': status = ReadX(line, ref counter, block, parameters); break;
                case 'y': status = ReadY(line, ref counter, block, parameters); break;
                case 'z': status = ReadZ(line, ref counter, block, parameters); break;
                case 'a': status = ReadA(line, ref counter, block, parameters); break;
                case 'b': status = ReadB(line, ref counter, block, parameters); break;
                case 'c': status = ReadC(line, ref counter, block, parameters); break;
                case 'u': status = ReadU(line, ref counter, block, parameters); break;
                case 'v': status = ReadV(line, ref counter, block, parameters); break;
                case 'd': status = ReadD(line, ref counter, block, parameters); break;
                case 'f': status = ReadF(line, ref counter, block, parameters); break;
                case 'h': status = ReadH(line, ref counter, block, parameters); break;
                case 'i': status = ReadI(line, ref counter, block, parameters); break;
                case 'j': status = ReadJ(line, ref counter, block, parameters); break;
                case 'k': status = ReadK(line, ref counter, block, parameters); break;
                case 'l': status = ReadL(line, ref counter, block, parameters); break;
                case 'm': status = ReadM(line, ref counter, block, parameters); break;
                case 'p': status = ReadP(line, ref counter, block, parameters); break;
                case 'q': status = ReadQ(line, ref counter, block, parameters); break;
                case 'r': status = ReadR(line, ref counter, block, parameters); break;
                case 's': status = ReadS(line, ref counter, block, parameters); break;
                case 't': status = ReadT(line, ref counter, block, parameters); break;
                case '(': status = ReadComment(line, ref counter, block, parameters); break;
                case '#': status = ReadParameter(line, ref counter, out _, parameters); break;
                case 'g': status = ReadG(line, ref counter, block, parameters); break;
                default:
                    return NCE_BAD_CHARACTER_USED;
            }

            return status;
        }






        /// <summary>
        /// Read line number “N”/“O”: unsigned int → block.line_number. :contentReference[oaicite:2]{index=2}</summary>
        private static int ReadLineNumber(string line, ref int counter, Block block)
        {
            if (line[counter] != 'n' && line[counter] != 'o')
                return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;

            int value;
            int status = ReadIntegerUnsigned(line, ref counter, out value);
            if (status != RS274NGC_OK) return status;

            block.line_number = value;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Unsigned integer: one or more digits (no sign). :contentReference[oaicite:3]{index=3}</summary>
        private static int ReadIntegerUnsigned(string line, ref int counter, out int result)
        {
            int start = counter;
            while (counter < line.Length && char.IsDigit(line[counter]))
                counter++;
            if (counter == start)
            {
                result = 0;
                return NCE_BAD_FORMAT_UNSIGNED_INTEGER;
            }
            if (!int.TryParse(line.Substring(start, counter - start), out result))
                return NCE_SSCANF_FAILED;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Integer (via real + floor/ceil check). :contentReference[oaicite:4]{index=4}</summary>
        private static int ReadIntegerValue(string line, ref int counter, out int result, double[] parameters)
        {
            double realVal;
            int status = ReadRealValue(line, ref counter, out realVal, parameters);
            if (status != RS274NGC_OK) { result = 0; return status; }

            result = (int)Math.Floor(realVal);
            double frac = realVal - result;
            if (frac > 0.9999) result = (int)Math.Ceiling(realVal);
            else if (frac > 0.0001) return NCE_NON_INTEGER_VALUE_FOR_INTEGER;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Read a real (number, [expr], parameter, or unary). :contentReference[oaicite:5]{index=5}</summary>
        private static int ReadRealValue(string line, ref int counter, out double result, double[] parameters)
        {
            if (counter >= line.Length) { result = 0; return NCE_NO_CHARACTERS_FOUND_IN_READING_REAL_VALUE; }
            char c = line[counter];
            int status;
            if (c == '[')
                status = ReadRealExpression(line, ref counter, out result, parameters);
            else if (c == '#')
                status = ReadParameter(line, ref counter, out result, parameters);
            else if ((c >= 'a' && c <= 'z') ||
                     (c == '-' && (counter + 1 < line.Length &&
                         !char.IsDigit(line[counter + 1]) && line[counter + 1] != '.')))
                status = ReadUnary(line, ref counter, out result, parameters);
            else
                status = ReadRealNumber(line, ref counter, out result);

            return status;
        }

        /// <summary>
        /// Number with optional sign & decimal. :contentReference[oaicite:6]{index=6}</summary>
        private static int ReadRealNumber(string line, ref int counter, out double result)
        {
            int n = counter;
            bool seenDigit = false, seenDot = false;

            // Leading +/- or first digit/dot
            if (line[n] == '+') { n++; counter++; }
            else if (line[n] == '-') { n++; }

            while (n < line.Length)
            {
                char c = line[n];
                if (char.IsDigit(c)) seenDigit = true;
                else if (c == '.' && !seenDot) { seenDot = true; }
                else break;
                n++;
            }
            if (!seenDigit) { result = 0; return NCE_NO_DIGITS_FOUND_WHERE_REAL_NUMBER_SHOULD_BE; }

            string token = line.Substring(counter, n - counter);
            if (!double.TryParse(token, out result)) return NCE_SSCANF_FAILED;
            counter = n;
            return RS274NGC_OK;
        }

        /// <summary>
        /// Fully general “[ … ]” expression with stack. :contentReference[oaicite:7]{index=7}</summary>
        private static int ReadRealExpression(string line, ref int counter, out double result, double[] parameters)
        {
            if (line[counter] != '[') { result = 0; return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED; }
            counter++;

            const int MAX_STACK = 5;
            double[] values = new double[MAX_STACK];
            int[] ops = new int[MAX_STACK];
            int si = 0;

            // Read first value and operation
            double status = ReadRealValue(line, ref counter, out values[0], parameters);
            if (status != RS274NGC_OK) { result = 0; return (int)status; }
            status = ReadOperation(line, ref counter, out ops[0]);
            if (status != RS274NGC_OK) { result = 0; return (int)status; }

            // Process until RIGHT_BRACKET on ops[0]
            while (ops[0] != RIGHT_BRACKET)
            {
                status = ReadRealValue(line, ref counter, out values[++si], parameters);
                if (status != RS274NGC_OK) { result = 0; return (int)status; }
                status = ReadOperation(line, ref counter, out ops[si]);
                if (status != RS274NGC_OK) { result = 0; return (int)status; }

                while (si > 0 && Precedence(ops[si]) <= Precedence(ops[si - 1]))
                {
                    status = ExecuteBinary(values[si - 1], ops[si - 1], values[si]);
                    if (status != RS274NGC_OK) { result = 0; return (int)status; }
                    ops[si - 1] = ops[si];
                    si--;
                }
            }
            result = values[0];
            return RS274NGC_OK;
        }

        /// <summary>
        /// Binary op: + - * / ** and, or, xor, mod, logicals, etc. :contentReference[oaicite:8]{index=8}</summary>
        private static int ReadOperation(string line, ref int counter, out int op)
        {
            op = 0;
            char c = line[counter++];
            switch (c)
            {
                case '+': op = PLUS; break;
                case '-': op = MINUS; break;
                case '/': op = DIVIDED_BY; break;
                case '*':
                    if (counter < line.Length && line[counter] == '*') { op = POWER; counter++; }
                    else op = TIMES;
                    break;
                case ']': op = RIGHT_BRACKET; break;
                case 'a':
                    if (MatchAhead(line, counter, "nd")) { op = AND2; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_A;
                    break;
                case 'm':
                    if (MatchAhead(line, counter, "od")) { op = MODULO; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_M;
                    break;
                case 'o':
                    if (MatchAhead(line, counter, "r")) { op = NON_EXCLUSIVE_OR; counter++; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_O;
                    break;
                case 'x':
                    if (MatchAhead(line, counter, "or")) { op = EXCLUSIVE_OR; counter += 2; }
                    else return NCE_UNKNOWN_OPERATION_NAME_STARTING_WITH_X;
                    break;
                case '<':
                    if (MatchAhead(line, counter, "=")) { op = LOGICAL_LE; counter++; }
                    else if (MatchAhead(line, counter, ">")) { op = LOGICAL_NE; counter++; }
                    else op = LOGICAL_LT;
                    break;
                case '>':
                    if (MatchAhead(line, counter, "=")) { op = LOGICAL_GE; counter++; }
                    else op = LOGICAL_GT;
                    break;
                case '=': op = LOGICAL_EQ; break;
                default: return NCE_UNKNOWN_OPERATION;
            }
            return RS274NGC_OK;
        }

        /// <summary>
        /// Match literal the rest of a token. </summary>
        private static bool MatchAhead(string s, int idx, string tok)
        {
            return idx + tok.Length <= s.Length && s.Substring(idx, tok.Length) == tok;
        }

        /// <summary>
        /// Unary op names: abs, acos, asin, atan, cos, exp, etc. :contentReference[oaicite:9]{index=9}</summary>
        private static int ReadUnary(string line, ref int counter, out double result, double[] parameters)
        {
            double operation;
            int status = ReadOperationUnary(line, ref counter, out operation);
            if (status != RS274NGC_OK) { result = 0; return status; }

            if (operation == UNEGATIVE)
            {
                status = ReadRealValue(line, ref counter, out result, parameters);
                if (status != RS274NGC_OK) return status;
            }
            else
            {
                if (counter >= line.Length || line[counter] != '[') { result = 0; return NCE_LEFT_BRACKET_MISSING_AFTER_UNARY_OPERATION_NAME; }
                status = ReadRealExpression(line, ref counter, out result, parameters);
                if (status != RS274NGC_OK) return status;
            }

            if (operation == ATAN)
                return ReadAtan(line, ref counter, ref result, parameters);
            else
                return ExecuteUnary(result, (int)operation);
        }

        /// <summary>
        /// Atan(…) has two args: value/[expr]. :contentReference[oaicite:10]{index=10}</summary>
        private static int ReadAtan(string line, ref int counter, ref double val, double[] parameters)
        {
            if (counter >= line.Length || line[counter] != '/') return NCE_SLASH_MISSING_AFTER_FIRST_ATAN_ARGUMENT;
            counter++;
            if (counter >= line.Length || line[counter] != '[') return NCE_LEFT_BRACKET_MISSING_AFTER_SLASH_WITH_ATAN;
            int status = ReadRealExpression(line, ref counter, out double arg2, parameters);
            if (status != RS274NGC_OK) return status;

            // C++: atan2(y, x) but their args are reversed
            val = Math.Atan2(val, arg2) * (180.0 / Math.PI);
            return RS274NGC_OK;
        }

        // --- Now the coordinate readers, all follow the same pattern ---

        private static int ReadX(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'x', v => { b.x_flag = true; b.x_number = v; });
        private static int ReadY(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'y', v => { b.y_flag = true; b.y_number = v; });
        private static int ReadZ(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'z', v => { b.z_flag = true; b.z_number = v; });
        private static int ReadA(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'a', v => { b.a_flag = true; b.a_number = v; });
        private static int ReadB(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'b', v => { b.b_flag = true; b.b_number = v; });
        private static int ReadC(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'c', v => { b.c_flag = true; b.c_number = v; });
        private static int ReadU(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'u', v => { b.u_flag = true; b.u_number = v; });
        private static int ReadV(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'v', v => { b.v_flag = true; b.v_number = v; });
        private static int ReadF(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'f', v => b.f_number = v, mustBePositive: true);
        private static int ReadG(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'g', v => { b.g_flag = true; b.g_number = v; });
        //private static int ReadH(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'h', v => { b.h_flag = true; b.h_number = v; });
        private static int ReadI(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'i', v => { b.i_flag = true; b.i_number = v; });
        private static int ReadJ(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'j', v => { b.j_flag = true; b.j_number = v; });
        private static int ReadK(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'k', v => { b.k_flag = true; b.k_number = v; });
        //private static int ReadL(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'l', v => { b.l_flag = true; b.l_number = v; });
        //private static int ReadM(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'm', v => { b.m_flag = true; b.m_number = v; });
        private static int ReadP(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'p', v => { b.p_flag = true; b.p_number = v; });
        private static int ReadQ(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'q', v => { b.q_flag = true; b.q_number = v; });
        private static int ReadR(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 'r', v => { b.r_flag = true; b.r_number = v; });
        private static int ReadS(string line, ref int counter, Block b, double[] p) => ReadAxis(line, ref counter, 's', v => b.s_number = v, mustBePositive: true);
        private static int ReadT(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 't') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.t_number != -1) return NCE_MULTIPLE_T_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int t, p);
            if (status != RS274NGC_OK) return status;
            b.t_number = t;
            return RS274NGC_OK;
        }
        private static int ReadD(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'd') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.d_flag) return NCE_MULTIPLE_D_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int d, p);
            if (status != RS274NGC_OK) return status;
            if (d < 0) return NCE_NEGATIVE_D_WORD_TOOL_RADIUS_INDEX_USED;
            if (d > 99999 && b.g_modes[14] != G_96) return NCE_TOOL_RADIUS_INDEX_TOO_BIG;
            b.d_number = d; b.d_flag = true;
            return RS274NGC_OK;
        }
        private static int ReadH(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'h') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.h_number > -1) return NCE_MULTIPLE_H_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int h, p);
            if (status != RS274NGC_OK) return status;
            if (h < 0) return NCE_NEGATIVE_H_WORD_TOOL_LENGTH_OFFSET_INDEX_USED;
            if (h > 99999) return NCE_TOOL_LENGTH_OFFSET_INDEX_TOO_BIG;
            b.h_number = h;
            return RS274NGC_OK;
        }
        private static int ReadL(string line, ref int counter, Block b, double[] p)
        {
            if (line[counter] != 'l') return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            if (b.l_flag) return NCE_MULTIPLE_L_WORDS_ON_ONE_LINE;
            int status = ReadIntegerValue(line, ref counter, out int l, p);
            if (status != RS274NGC_OK) return status;
            if (l < 0) return NCE_NEGATIVE_L_WORD_USED;
            b.l_flag = true; b.l_number = l;
            return RS274NGC_OK;
        }
        private static int ReadM(string line, ref int counter, Block b, double[] p)
        {
            // 1) Must start with an 'm'
            if (line[counter] != 'm')
                return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;

            // 2) Delegate the numeric parsing to ReadI (ref counter, Block, and p)
            int status = ReadI(line, ref counter, b, p);
            if (status != RS274NGC_OK)
                return status;

            // 3) Pull the code back out of the Block
            int mCode = (int)b.i_number;
            if (mCode < 0 || mCode > 119)
                return NCE_M_CODE_GREATER_THAN_119;

            // 4) Modal-group lookup (using your m_modal_group[])
            int modal = m_modal_group[mCode];
            if (modal == -1)
                return NCE_UNKNOWN_M_CODE_USED;
            if (b.m_modes[modal] != -1)
                return NCE_TWO_M_CODES_USED_FROM_SAME_MODAL_GROUP;

            // 5) Record the code in the block
            b.m_modes[modal] = mCode;
            b.m_count++;

            return RS274NGC_OK;
        }


        // Helper for all single-letter real-value readers
        private static int ReadAxis(string line, ref int counter, char letter, Action<double> assign, bool mustBePositive = false)
        {
            if (line[counter] != letter) return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            counter++;
            double val;
            int status = ReadRealValue(line, ref counter, out val, _setup.parameters);
            if (status != RS274NGC_OK) return status;
            if (mustBePositive && val < 0.0) return letter == 'f'
                ? NCE_NEGATIVE_F_WORD_USED
                : NCE_NEGATIVE_SPINDLE_SPEED_USED;
            assign(val);
            return RS274NGC_OK;
        }

        private static int ConvertComment(string comment)
        {
            int status = 0;
            int i = comment.IndexOf('(');
            int n = comment.IndexOf(')');
            while (i >= 0 && n > i)
            {
                string inner = comment.Substring(i + 1, n - i - 1);
                status = ConvertComment2(inner);
                if (status != 0) return status;
                i = comment.IndexOf('(', n + 1);
                n = comment.IndexOf(')', n + 1);
            }
            return status;
        }

        private static int ConvertComment2(string comment)
        {
            int m = 0;
            // skip leading space/tab
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length)
            {
                Comment(comment);
                return 0;
            }
            char c = comment[m];
            // must start MSG,
            if (char.ToUpperInvariant(c) != 'M')
            {
                Comment(comment);
                return 0;
            }
            // skip 'M'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || char.ToUpperInvariant(comment[m]) != 'S')
            {
                Comment(comment);
                return 0;
            }
            // skip 'S'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || char.ToUpperInvariant(comment[m]) != 'G')
            {
                Comment(comment);
                return 0;
            }
            // skip 'G'
            m++;
            while (m < comment.Length && (comment[m] == ' ' || comment[m] == '\t')) m++;
            if (m >= comment.Length || comment[m] != ',')
            {
                Comment(comment);
                return 0;
            }
            // MSG, so print after comma
            Message(comment.Substring(m + 1));
            return 0;
        }

        // === feed mode (G93/94/95) ===

        private static int ConvertFeedMode(int code, SetupData s)
        {
            switch (code)
            {
                case G_93:
                    Comment("interpreter: feed mode set to inverse time");
                    s.feed_mode = (int)RS274NGC_FEED_MODE.INVERSE_TIME;
                    break;
                case G_94:
                    Comment("interpreter: feed mode set to units per minute");
                    s.feed_mode = (int)RS274NGC_FEED_MODE.PER_MINUTE;
                    break;
                case G_95:
                    Comment("interpreter: feed mode set to units per rev");
                    s.feed_mode = (int)RS274NGC_FEED_MODE.PER_REV;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G93_OR_G94_OR_G95");
            }
            return 0;
        }

        // === feed rate (F…) ===

        private static int ConvertFeedRate(Block b, SetupData s)
        {
            SetFeedRate(b.f_number);
            s.feed_rate = b.f_number;
            return 0;
        }

        // === spindle mode (G96/G97) ===

        private static int ConvertSpindleMode(int code, SetupData s)
        {
            switch (code)
            {
                case G_96:
                    Canon.SET_SPINDLE_MODE(CANON_SPINDLE_MODE.CANON_SPINDLE_CSS);
                    s.spindle_mode = (int)CANON_SPINDLE_MODE.CANON_SPINDLE_CSS;
                    break;
                case G_97:
                    Canon.SET_SPINDLE_MODE(CANON_SPINDLE_MODE.CANON_SPINDLE_NORMAL);
                    s.spindle_mode = (int)CANON_SPINDLE_MODE.CANON_SPINDLE_NORMAL;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G96_OR_G97");
            }
            return 0;
        }

        // === spindle speed (S…) ===

        private static int ConvertSpeed(Block b, SetupData s)
        {
            Canon.SET_SPINDLE_SPEED(b.s_number);
            s.spindle_speed = (int)b.s_number;
            return 0;
        }

        // === tool select (T…) ===

        private static int ConvertToolSelect(Block b, SetupData s)
        {
            // mirror C++: ConvertToolToIndex(settings, number, &index)
            int index = LookupToolIndex(s, (int)b.t_number);
            s.selected_tool_slot = index;
            return 0;
        }

        // === M-codes (tool change, spindle on/off, coolant, overrides…) ===

        private static int ConvertM(Block b, SetupData s)
        {
            // 1) Tool change (M6)
            if (b.g_modes[6] != -1)
            {
                _setup.feed_rate = Canon.GET_EXTERNAL_FEEDRATE();

            }
            // 2) Spindle start/stop
            switch (b.g_modes[7])
            {
                case 3:
                    Canon.START_SPINDLE_CLOCKWISE();
                    s.spindle_turning = (int)CANON_DIRECTION.CANON_CLOCKWISE;
                    break;
                case 4:
                    Canon.START_SPINDLE_COUNTERCLOCKWISE();
                    s.spindle_turning = (int)CANON_DIRECTION.CANON_COUNTERCLOCKWISE;
                    break;
                case 5:
                    Canon.STOP_SPINDLE_TURNING();
                    s.spindle_turning = (int)CANON_DIRECTION.CANON_STOPPED;
                    break;
            }
            // 3) Coolant
            switch (b.g_modes[8])
            {
                case 7:
                    MistOn();
                    s.mist = true;
                    CoolantState(ON);
                    break;
                case 8:
                    FloodOn();
                    s.flood = true;
                    CoolantState(ON);
                    break;
                case 9:
                    MistOff();
                    FloodOff();
                    s.mist = false;
                    CoolantState(OFF);
                    s.flood = false;
                    CoolantState(OFF);
                    break;
            }
            // 4) Overrides (M48/M49)
            if (b.g_modes[9] == 48)
            {
                EnableFeedOverride();
                EnableSpeedOverride();
                s.feed_override = true;
                s.speed_override = true;
            }
            else if (b.g_modes[9] == 49)
            {
                if (b.p_flag && b.p_number == 1)
                {
                    DisableFeedOverride();
                    EnableSpeedOverride();
                    s.feed_override = false;
                    s.speed_override = true;
                }
                else if (b.p_flag && b.p_number == 2)
                {
                    EnableFeedOverride();
                    DisableSpeedOverride();
                    s.feed_override = true;
                    s.speed_override = false;
                }
                else if (b.p_flag)
                {
                    throw new InvalidOperationException("NCE_INVALID_PWORD_M49");
                }
                else
                {
                    DisableFeedOverride();
                    DisableSpeedOverride();
                    s.feed_override = false;
                    s.speed_override = false;
                }
            }
            // optional M100, etc.
            if (b.g_modes[10] != -1)
            {
                M100(b.g_modes[10]);
            }
            return 0;
        }

        // === G-codes (all non-modal motions) ===

        private static int ConvertG(Block b, SetupData s)
        {
            // 1) dwell (G4)
            if (b.g_modes[0] == G_4)
                ConvertDwell(b.p_number);

            // 2) plane select (G17/G18/G19)
            if (b.g_modes[2] != -1)
                ConvertSetPlane(b.g_modes[2], s);

            // 3) length units (G20/G21)
            if (b.g_modes[6] != -1)
                ConvertLengthUnits(b.g_modes[6], s);

            // 4) cutter comp (G40/G41/G42)
            if (b.g_modes[7] != -1)
                ConvertCutterCompensation(b.g_modes[7], b, s);

            // 5) tool length offset (G43/G49)
            if (b.g_modes[8] != -1)
                ConvertToolLengthOffset(b.g_modes[8], b, s);

            // 6) coordinate system (G54–G59.s)
            if (b.g_modes[12] != -1)
                ConvertCoordinateSystem(b.g_modes[12], s);

            // 7) control mode (G61/G61.1/G64)
            if (b.g_modes[13] != -1)
                ConvertControlMode(b.g_modes[13], s);

            // 8) distance mode (G90/G91)
            if (b.g_modes[3] != -1)
                Convertdistance_mode(b.g_modes[3], s);

            // 9) retract mode (G98/G99)
            if (b.g_modes[10] != -1)
                ConvertRetractMode(b.g_modes[10], s);

            // 10) modal-0 codes (G10, G28, G30, G92, …)
            if (b.g_modes[0] != -1)
                ConvertModal0(b.g_modes[0], b, s);

            // 11) any implicit or explicit motion (G0, G1, G2, G3, canned, etc.)
            if (b.motion_to_be != -1)
                ConvertMotion(b.motion_to_be, b, s);

            return 0;
        }

        // === stopping codes (M0, M1, M2, M30, M60) ===

        private static int ConvertStop(Block b, SetupData s)
        {
            int m = b.g_modes[0];
            if (m == 0 || m == 1 || m == 2 || m == 30 || m == 60)
                return EXIT_CODE;    // signal interpreter exit
            else
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_M0_M1_M2_M30_M60");
        }

        // === the simple helpers from convert_g ===

        private static int ConvertControlMode(int gCode, SetupData s)
        {
            switch (gCode)
            {
                case G_61:
                    SetMotionControlMode((int)CANON_MOTION_MODE.CANON_EXACT_PATH);
                    s.control_mode = (int)CANON_MOTION_MODE.CANON_EXACT_PATH;
                    break;
                case G_61_1:
                    SetMotionControlMode((int)CANON_MOTION_MODE.CANON_EXACT_STOP);
                    s.control_mode = (int)CANON_MOTION_MODE.CANON_EXACT_STOP;
                    break;
                case G_64:
                    SetMotionControlMode((int)CANON_MOTION_MODE.CANON_CONTINUOUS);
                    s.control_mode = (int)CANON_MOTION_MODE.CANON_CONTINUOUS;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G61_G61_1_OR_G64");
            }
            return 0;
        }

        private static int ConvertDistanceMode(int code, SetupData s)
        {
            switch (code)
            {
                case G_90:
                    s.distance_mode = (int)RS274NGC_DISTANCE_MODE.MODE_ABSOLUTE;
                    break;
                case G_91:
                    s.distance_mode = (int)RS274NGC_DISTANCE_MODE.MODE_INCREMENTAL;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G90_OR_G91");
            }
            return 0;
        }

        private static int ConvertDwell(double time)
        {
            DWELL(time);
            return 0;
        }

        private static int ConvertLengthUnits(int code, SetupData s)
        {
            switch (code)
            {
                case G_20:
                    s.length_units = CANON_UNITS.Inches;
                    break;
                case G_21:
                    s.length_units = CANON_UNITS.Mm;
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G20_OR_G21");
            }
            return 0;
        }

        private static int ConvertModal0(int code, Block b, SetupData s)
        {
            switch (code)
            {
                case G_10:
                    ConvertSetup(b, s);
                    break;
                case G_28:
                case G_30:
                    ConvertHome(code, b, s);
                    break;
                case G_92:
                case G_92_1:
                case G_92_2:
                case G_92_3:
                case G_52:
                    ConvertAxisOffsets(code, b, s);
                    break;
                // G4 and G53 handled elsewhere
                case G_4:
                case G_53:
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G4_G10_G28_G30_G53_OR_G92_SERIES");
            }
            return 0;
        }



        /// <summary>
        /// G10: Set a program‐origin offset for the selected CS. :contentReference[oaicite:0]{index=0}</summary>
        private static int ConvertSetup(Block b, SetupData s)
        {
            // G10: set program‐origin offset
            int pInt = (int)(b.p_number + 0.0001);
            double[] pars = s.parameters;
            // re-create the locals...
            double x = b.x_flag ? b.x_number : pars[5201 + pInt * 20];
            double y = b.y_flag ? b.y_number : pars[5202 + pInt * 20];
            double z = b.z_flag ? b.z_number : pars[5203 + pInt * 20];
            double a = b.a_flag ? b.a_number : pars[5204 + pInt * 20];
            double bb = b.b_flag ? b.b_number : pars[5205 + pInt * 20];
            double c = b.c_flag ? b.c_number : pars[5206 + pInt * 20];
            double u = b.u_flag ? b.u_number : pars[5207 + pInt * 20];
            double v = b.v_flag ? b.v_number : pars[5208 + pInt * 20];
            // Helper to apply one axis
            void ApplyAxis(bool flag, double value, int baseCode)
            {
                if (!flag) return;
                int idx = baseCode + pInt * 20;
                // Notify that parameter [idx] changed:
                PChanged(idx.ToString());
                // Store the new offset
                pars[idx] = value;
            }

            ApplyAxis(b.x_flag, b.x_number, 5201);
            ApplyAxis(b.y_flag, b.y_number, 5202);
            ApplyAxis(b.z_flag, b.z_number, 5203);
            ApplyAxis(b.a_flag, b.a_number, 5204);
            ApplyAxis(b.b_flag, b.b_number, 5205);
            ApplyAxis(b.c_flag, b.c_number, 5206);
            ApplyAxis(b.u_flag, b.u_number, 5207);
            ApplyAxis(b.v_flag, b.v_number, 5208);

            // If this is the active CS, adjust current_* and origin_offset_*
            if (pInt == s.origin_index)
            {
                s.current_x += s.origin_offset_x;
                s.current_y += s.origin_offset_y;
                s.current_z += s.origin_offset_z;
                s.AA_current += s.AA_origin_offset;
                s.BB_current += s.BB_origin_offset;
                s.CC_current += s.CC_origin_offset;
                s.UU_current += s.UU_origin_offset;
                s.VV_current += s.VV_origin_offset;

                // set new origin
                s.origin_offset_x = x;
                s.origin_offset_y = y;
                s.origin_offset_z = z;
                s.AA_origin_offset = a;
                s.BB_origin_offset = bb;
                s.CC_origin_offset = c;
                s.UU_origin_offset = u;
                s.VV_origin_offset = v;

                s.current_x -= x;
                s.current_y -= y;
                s.current_z -= z;
                s.AA_current -= a;
                s.BB_current -= bb;
                s.CC_current -= c;
                s.UU_current -= u;
                s.VV_current -= v;

                // Emit the canonical “SET_ORIGIN_OFFSETS”
                Canon.SET_ORIGIN_OFFSETS(
                    x + s.axis_offset_x,
                    y + s.axis_offset_y,
                    z + s.axis_offset_z,
                    a + s.AA_axis_offset,
                    bb + s.BB_axis_offset,
                    c + s.CC_axis_offset,
                    u + s.UU_axis_offset,
                    v + s.VV_axis_offset
                );
            }
            return RS274NGC_OK;
        }

        /// <summary>
        /// G28/G30: Go to an intermediate point then home. :contentReference[oaicite:1]{index=1}</summary>
        private static int ConvertHome(int move, Block b, SetupData s)
        {
            // compute ends of first segment
            FindEnds(b, s, out double endX, out double endY, out double endZ, out double AA_end, out double BB_end, out double CC_end, out double UU_end, out double VV_end);

            if (s.cutter_comp_side != (int)cutter_comp.OFF)
                throw new InvalidOperationException("NCE_CANNOT_USE_G28_OR_G30_WITH_CUTTER_RADIUS_COMP");

            // rapid‐traverse to block point
            Canon.STRAIGHT_TRAVERSE(endX, endY, endZ, AA_end, BB_end, CC_end, UU_end, VV_end);

            if (move == G_28 || move == G_30)
            {
                // choose the right parameter block based on G-code
                int baseIdx = (move == G_28) ? 5161 : 5181;

                FindRelative(
                    s.parameters[baseIdx], s.parameters[baseIdx + 1], s.parameters[baseIdx + 2],
                    s.parameters[baseIdx + 3], s.parameters[baseIdx + 4], s.parameters[baseIdx + 5],
                    s.parameters[baseIdx + 6], s.parameters[baseIdx + 7],
                    out endX, out endY, out endZ,
                    out AA_end, out BB_end, out CC_end,
                    out UU_end, out VV_end,
                    s
                );
            }
            else
            {
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G28_OR_G30");
            }

            // rapid‐traverse to home and update current_*
            Canon.STRAIGHT_TRAVERSE(endX, endY, endZ, AA_end, BB_end, CC_end, UU_end, VV_end);
            s.current_x = endX; s.current_y = endY; s.current_z = endZ;
            s.AA_current = AA_end; s.BB_current = BB_end; s.CC_current = CC_end;
            s.UU_current = UU_end; s.VV_current = VV_end;

            return RS274NGC_OK;
        }

        /// <summary>
        /// G40/G41/G42: Tool‐radius compensation. :contentReference[oaicite:2]{index=2}</summary>
        private static int ConvertCutterCompensation(int code, Block b, SetupData s)
        {
            switch (code)
            {
                case G_40:
                    ConvertCutterCompensationOff(s);
                    break;
                case G_41:
                    ConvertCutterCompensationOn(cutter_comp.LEFT, b, s);
                    break;
                case G_42:
                    ConvertCutterCompensationOn(cutter_comp.RIGHT, b, s);
                    break;
                default:
                    throw new InvalidOperationException("NCE_BUG_CODE_NOT_G40_G41_OR_G42");
            }
            return RS274NGC_OK;
        }

        private static int ConvertCutterCompensationOff(SetupData s)
        {
            // interpreter comment omitted in release
            s.cutter_comp_side = (int)cutter_comp.OFF;
            if (s.program_x != double.NaN)
            {
                s.current_x = s.program_x;
                s.current_y = s.program_y;
                s.program_x = double.NaN;
                s.pending_comp_x = double.NaN;
            }
            return RS274NGC_OK;
        }

        private static int ConvertCutterCompensationOn(cutter_comp side, Block b, SetupData s)
        {
            if (s.plane != (int)CANON_PLANE.XY)
                throw new InvalidOperationException("NCE_CANNOT_TURN_CUTTER_RADIUS_COMP_ON_OUT_OF_XY_PLANE");
            if (s.cutter_comp_side != (int)cutter_comp.OFF)
                throw new InvalidOperationException("NCE_CANNOT_TURN_CUTTER_RADIUS_COMP_ON_WHEN_ON");

            // set up compensation using the current tool table diameter
            double radius = s.tool_table![s.selected_tool_slot].Diameter / 2.0;
            s.cutter_comp_radius = radius;
            s.cutter_comp_side = (int)side;
            s.program_x = s.current_x;  // remember un‐compensated
            s.program_y = s.current_y;
            s.pending_comp_x = double.NaN;
            return RS274NGC_OK;
        }

        /// <summary>
        /// G98/G99: Retract mode for canned cycles. :contentReference[oaicite:3]{index=3}</summary>
        private static int ConvertRetractMode(int code, SetupData s)
        {
            if (code == G_98) s.retract_mode = (int)RetractMode.OldZ;
            else if (code == G_99) s.retract_mode = (int)RetractMode.RPlane;
            else throw new InvalidOperationException("NCE_BUG_CODE_NOT_G98_OR_G99");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G17/G18/G19: Plane select. :contentReference[oaicite:4]{index=4}</summary>
        private static int ConvertSetPlane(int code, SetupData s)
        {
            if (code == G_17)
            {
                Canon.SELECT_PLANE(CANON_PLANE.XY);
                s.plane = (int)CANON_PLANE.XY;
            }
            else if (code == G_18)
            {
                if (s.cutter_comp_side != (int)cutter_comp.OFF)
                    throw new InvalidOperationException("NCE_CANNOT_USE_XZ_PLANE_WITH_CUTTER_RADIUS_COMP");
                Canon.SELECT_PLANE(CANON_PLANE.XZ);
                s.plane = (int)CANON_PLANE.XZ;
            }
            else if (code == G_19)
            {
                if (s.cutter_comp_side != (int)cutter_comp.OFF)
                    throw new InvalidOperationException("NCE_CANNOT_USE_YZ_PLANE_WITH_CUTTER_RADIUS_COMP");
                Canon.SELECT_PLANE(CANON_PLANE.YZ);
                s.plane = (int)CANON_PLANE.YZ;
            }
            else
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G17_G18_OR_G19");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G54 – G59.3: Coordinate-system select. :contentReference[oaicite:5]{index=5}</summary>
        private static int ConvertCoordinateSystem(int code, SetupData s)
        {
            int origin = code switch
            {
                G_54 => 1,
                G_55 => 2,
                G_56 => 3,
                G_57 => 4,
                G_58 => 5,
                G_59 => 6,
                G_59_1 => 7,
                G_59_2 => 8,
                G_59_3 => 9,
                _ => throw new InvalidOperationException("NCE_BUG_CODE_NOT_IN_RANGE_G54_TO_G593")
            };

            // if already in that CS with same units, no-op
            if (s.origin_index == origin && (int)s.length_units_of_origin == (int)s.length_units)
                return RS274NGC_OK;

            s.origin_index = origin;
            s.length_units_of_origin = s.length_units;
            int idx = 5220;
            PChanged(idx.ToString());
            s.parameters[idx] = origin;

            // shift current_* by old origin, then subtract new origin
            s.current_x += s.origin_offset_x;
            s.current_y += s.origin_offset_y;
            s.current_z += s.origin_offset_z;
            s.AA_current += s.AA_origin_offset;
            s.BB_current += s.BB_origin_offset;
            s.CC_current += s.CC_origin_offset;
            s.UU_current += s.UU_origin_offset;
            s.VV_current += s.VV_origin_offset;

            // load new origin offsets from parameters
            int i = (origin - 1) * 20;
            s.origin_offset_x = s.parameters[5201 + i];
            s.origin_offset_y = s.parameters[5202 + i];
            s.origin_offset_z = s.parameters[5203 + i];
            s.AA_origin_offset = s.parameters[5204 + i];
            s.BB_origin_offset = s.parameters[5205 + i];
            s.CC_origin_offset = s.parameters[5206 + i];
            s.UU_origin_offset = s.parameters[5207 + i];
            s.VV_origin_offset = s.parameters[5208 + i];

            // subtract off new origin
            s.current_x -= s.origin_offset_x;
            s.current_y -= s.origin_offset_y;
            s.current_z -= s.origin_offset_z;
            s.AA_current -= s.AA_origin_offset;
            s.BB_current -= s.BB_origin_offset;
            s.CC_current -= s.CC_origin_offset;
            s.UU_current -= s.UU_origin_offset;
            s.VV_current -= s.VV_origin_offset;

            // canonical “SET_ORIGIN_OFFSETS”
            Canon.SET_ORIGIN_OFFSETS(
                s.origin_offset_x + s.axis_offset_x,
                s.origin_offset_y + s.axis_offset_y,
                s.origin_offset_z + s.axis_offset_z,
                s.AA_origin_offset + s.AA_axis_offset,
                s.BB_origin_offset + s.BB_axis_offset,
                s.CC_origin_offset + s.CC_axis_offset,
                s.UU_origin_offset + s.UU_axis_offset,
                s.VV_origin_offset + s.VV_axis_offset
            );
            return RS274NGC_OK;
        }

        /// <summary>
        /// Dispatch any motion: G0/G1/G2/G3/G38.2/G80/G81–G89. :contentReference[oaicite:7]{index=7}</summary>
        private static int ConvertMotion(int motion, Block b, SetupData s)
        {
            switch (motion)
            {
                case G_0:  // rapid‐traverse
                case G_1:  // feed
                           // distance vs inverse‐time handled upstream
                    return RS274NGC_OK;
                case G_2:
                case G_3:
                    ConvertArc(motion, b, s);
                    return RS274NGC_OK;
                case G_32:
                    ConvertThread(motion, b, s);
                    return RS274NGC_OK;
                case G_38_2:
                    ConvertProbe(b, s);
                    return RS274NGC_OK;
                case G_80:
                    // Cancel canned cycle: no‐op
                    return RS274NGC_OK;
                default:
                    if (motion >= G_81 && motion <= G_89)
                    {
                        ConvertCycle(motion, b, s);
                        return RS274NGC_OK;
                    }
                    throw new InvalidOperationException("NCE_BUG_UNKNOWN_MOTION_CODE");
            }
        }

        /// <summary>
        /// G38.2: Single‐point probe with straight‐feed. :contentReference[oaicite:8]{index=8}</summary>
        private static int ConvertProbe(Block b, SetupData s)
        {
            if (s.feed_mode == (int)RS274NGC_FEED_MODE.INVERSE_TIME)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_IN_INVERSE_TIME_FEED_MODE");
            if (s.cutter_comp_side != (int)cutter_comp.OFF)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_WITH_CUTTER_RADIUS_COMP_ON");
            if (s.feed_rate == 0.0)
                throw new InvalidOperationException("NCE_CANNOT_PROBE_WITH_ZERO_FEED_RATE");

            // Ensure no rotary movement
            FindEnds(b, s, out double ex, out double ey, out double ez, out double eA, out double eB, out double eC, out double eU, out double eV);
            double dist = Math.Sqrt(
                Math.Pow(s.current_x - ex, 2) +
                Math.Pow(s.current_y - ey, 2) +
                Math.Pow(s.current_z - ez, 2)
            );
            if (dist < ((s.length_units == CANON_UNITS.Mm) ? 0.254 : 0.01))
                throw new InvalidOperationException("NCE_START_POINT_TOO_CLOSE_TO_PROBE_POINT");

            TurnProbeOn();
            StraightProbe(ex, ey, ez, eA, eB, eC, eU, eV);
            TurnProbeOff();
            s.motion_mode = G_38_2;
            _setup.probe_flag = probe_flag.ON;
            return RS274NGC_OK;
        }

        /// <summary>
        /// G81–G89: Canned‐cycle dispatch. :contentReference[oaicite:9]{index=9}</summary>
        private static int ConvertCycle(int motion, Block b, SetupData s)
        {
            var plane = (CANON_PLANE)s.plane;
            double x       =  b.x_number;
            double y       =  b.y_number;
            double clearZ  =  b.clear_z;
            double bottomZ =  b.bottom_z;
            double P       =  b.p_number;
            double delta   =  b.r_number;
            double offsetX =  s.axis_offset_x;
            double offsetY =  s.axis_offset_y;
            double middleZ =  s.mid_offset_Z;
            var direction = (CANON_DIRECTION)b.direction;      // or b.direction if you stored it there 
            var feedMode  = (CANON_SPEED_FEED_MODE)s.feed_mode;
            // you would dispatch into G81, G82…G89 here, e.g.:
            if (motion == G_81) ConvertCycleG81(plane, x, y, clearZ, bottomZ);
            else if (motion == G_82) ConvertCycleG82(plane, x, y, clearZ, bottomZ, P);
            else if (motion == G_83) ConvertCycleG83(plane, x, y, clearZ, bottomZ, P, delta, direction, feedMode);
            else if (motion == G_84) ConvertCycleG84(plane, x, y, delta, clearZ, bottomZ, direction, feedMode);
            else if (motion == G_85) ConvertCycleG85(plane, x, y, clearZ, bottomZ, P);
            else if (motion == G_86) ConvertCycleG86(plane, x, y, clearZ, bottomZ, P, direction);
            else if (motion == G_87) ConvertCycleG87(plane, x, offsetX, y, offsetY, delta, clearZ, middleZ, bottomZ, direction);
            else if (motion == G_88) ConvertCycleG88(plane, x, y, bottomZ, P, direction);
            else if (motion == G_89) ConvertCycleG89(plane, x, y, clearZ, bottomZ, P);

            else throw new InvalidOperationException("NCE_BUG_UNKNOWN_CANNED_CYCLE");
            return RS274NGC_OK;
        }

        /// <summary>
        /// G76/G32: Threading or custom cycles. :contentReference[oaicite:10]{index=10}</summary>
        private static int ConvertThread(int motion, Block b, SetupData s)
        {
            motion = (int)s.plane;  
            // G32—single‐point threading:
            if (motion == G_32)
                return ConvertCycle(motion, b, s);
            // G76—multi‐pass threading (example):
            else if (motion == G_76)
                return ConvertCycleG76(motion, b.x_number, b.z_number, b.r_number, b.p_number, b.q_number);
            else
                throw new InvalidOperationException("NCE_BUG_UNKNOWN_THREAD_CYCLE");    
            //return RS274NGC_OK;
        }
        private static int FindEnds(Block b, SetupData s, out double px, out double py, out double pz, out double aa, out double bb, out double cc, out double uu, out double vv)
        {
            int mode = (int)s.distance_mode;
            bool middle = !double.IsNaN(s.program_x);
            bool comp = s.cutter_comp_side != (int)cutter_comp.OFF;

            // G53: machine coordinates
            if (b.g_modes[0] == G_53)
            {
                px = b.x_flag ? b.x_number - (s.tool_xoffset + s.origin_offset_x + s.axis_offset_x) : s.current_x;
                py = b.y_flag ? b.y_number - (s.tool_yoffset + s.origin_offset_y + s.axis_offset_y) : s.current_y;
                pz = b.z_flag ? b.z_number - (s.tool_length_offset + s.origin_offset_z + s.axis_offset_z) : s.current_z;
                aa = b.a_flag ? b.a_number - (s.AA_origin_offset + s.AA_axis_offset) : s.AA_current;
                bb = b.b_flag ? b.b_number - (s.BB_origin_offset + s.BB_axis_offset) : s.BB_current;
                cc = b.c_flag ? b.c_number - (s.CC_origin_offset + s.CC_axis_offset) : s.CC_current;
                uu = b.u_flag ? b.u_number - (s.UU_origin_offset + s.UU_axis_offset) : s.UU_current;
                vv = b.v_flag ? b.v_number - (s.VV_origin_offset + s.VV_axis_offset) : s.VV_current;
            }
            // Absolute mode
            else if (mode == (int)RS274NGC_DISTANCE_MODE.MODE_ABSOLUTE)
            {
                px = b.x_flag ? b.x_number : (comp && middle ? s.program_x : s.current_x);
                py = b.y_flag ? b.y_number : (comp && middle ? s.program_y : s.current_y);
                pz = b.z_flag ? b.z_number : (comp && middle ? s.program_z : s.current_z);
                aa = b.a_flag ? b.a_number : s.AA_current;
                bb = b.b_flag ? b.b_number : s.BB_current;
                cc = b.c_flag ? b.c_number : s.CC_current;
                uu = b.u_flag ? b.u_number : s.UU_current;
                vv = b.v_flag ? b.v_number : s.VV_current;
            }
            // Incremental mode
            else
            {
                px = b.x_flag ? (comp && middle ? b.x_number + s.program_x : b.x_number + s.current_x) : (comp && middle ? s.program_x : s.current_x);
                py = b.y_flag ? (comp && middle ? b.y_number + s.program_y : b.y_number + s.current_y) : (comp && middle ? s.program_y : s.current_y);
                pz = b.z_flag ? (s.current_z + b.z_number) : s.current_z;
                aa = b.a_flag ? (s.AA_current + b.a_number) : s.AA_current;
                bb = b.b_flag ? (s.BB_current + b.b_number) : s.BB_current;
                cc = b.c_flag ? (s.CC_current + b.c_number) : s.CC_current;
                uu = b.u_flag ? (s.UU_current + b.u_number) : s.UU_current;
                vv = b.v_flag ? (s.VV_current + b.v_number) : s.VV_current;
            }

            return RS274NGC_OK;
        }

        /// <summary>
        /// Convert an absolute point (x1…vv1) into a relative point under current tool offsets. :contentReference[oaicite:1]{index=1}</summary>
        private static int FindRelative(double x1, double y1, double z1, double aa1, double bb1, double cc1, double uu1, double vv1, out double x2, out double y2, out double z2, out double aa2, out double bb2, out double cc2, out double uu2, out double vv2, SetupData s)
        {
            x2 = x1 - (s.tool_xoffset + s.origin_offset_x + s.axis_offset_x);
            y2 = y1 - (s.tool_yoffset + s.origin_offset_y + s.axis_offset_y);
            z2 = z1 - (s.tool_length_offset + s.origin_offset_z + s.axis_offset_z);

            aa2 = aa1 - (s.AA_origin_offset + s.AA_axis_offset);
            bb2 = bb1 - (s.BB_origin_offset + s.BB_axis_offset);
            cc2 = cc1 - (s.CC_origin_offset + s.CC_axis_offset);
            uu2 = uu1 - (s.UU_origin_offset + s.UU_axis_offset);
            vv2 = vv1 - (s.VV_origin_offset + s.VV_axis_offset);

            return RS274NGC_OK;
        }


        /// <summary>
        /// Implements G52/G92 axis‐offset logic exactly as in the C++ reference. :contentReference[oaicite:2]{index=2}</summary>
        private static int ConvertAxisOffsets(int gCode, Block b, SetupData s)
        {
            if (s.cutter_comp_side != (int)cutter_comp.OFF)
                throw new InvalidOperationException("NCE_CANNOT_CHANGE_AXIS_OFFSETS_WITH_CUTTER_RADIUS_COMP");

            // Helper to mark a parameter dirty
            int PChanged(int idx)
            {
                s.ParamChanges[s.n_ParamChanges++] = idx;
                return idx;
            }

            if (gCode == G_92)
            {
                // Incremental origin shift
                if (b.x_flag)
                {
                    s.axis_offset_x = s.current_x + s.axis_offset_x - b.x_number;
                    s.current_x = b.x_number;
                    s.parameters[PChanged(5211)] = s.axis_offset_x;
                }
                if (b.y_flag)
                {
                    s.axis_offset_y = s.current_y + s.axis_offset_y - b.y_number;
                    s.current_y = b.y_number;
                    s.parameters[PChanged(5212)] = s.axis_offset_y;
                }
                if (b.z_flag)
                {
                    s.axis_offset_z = s.current_z + s.axis_offset_z - b.z_number;
                    s.current_z = b.z_number;
                    s.parameters[PChanged(5213)] = s.axis_offset_z;
                }
                if (b.a_flag)
                {
                    s.AA_axis_offset = s.AA_current + s.AA_axis_offset - b.a_number;
                    s.AA_current = b.a_number;
                    s.parameters[PChanged(5214)] = s.AA_axis_offset;
                }
                if (b.b_flag)
                {
                    s.BB_axis_offset = s.BB_current + s.BB_axis_offset - b.b_number;
                    s.BB_current = b.b_number;
                    s.parameters[PChanged(5215)] = s.BB_axis_offset;
                }
                if (b.c_flag)
                {
                    s.CC_axis_offset = s.CC_current + s.CC_axis_offset - b.c_number;
                    s.CC_current = b.c_number;
                    s.parameters[PChanged(5216)] = s.CC_axis_offset;
                }

                // propagate origin offsets and emit
                Canon.SET_ORIGIN_OFFSETS(
                    s.origin_offset_x + s.axis_offset_x,
                    s.origin_offset_y + s.axis_offset_y,
                    s.origin_offset_z + s.axis_offset_z,
                    s.AA_origin_offset + s.AA_axis_offset,
                    s.BB_origin_offset + s.BB_origin_offset,
                    s.CC_origin_offset + s.CC_axis_offset,
                    s.UU_origin_offset + s.UU_axis_offset,
                    s.VV_origin_offset + s.VV_axis_offset
                );
            }
            else if (gCode == G_52)
            {
                // Absolute axis‐offset set
                if (b.x_flag) { s.axis_offset_x = b.x_number; s.current_x = s.current_x; }
                if (b.y_flag) { s.axis_offset_y = b.y_number; s.current_y = s.current_y; }
                if (b.z_flag) { s.axis_offset_z = b.z_number; s.current_z = s.current_z; }
                if (b.a_flag) { s.AA_axis_offset = b.a_number; s.AA_current = s.AA_current; }
                if (b.b_flag) { s.BB_axis_offset = b.b_number; s.BB_current = s.BB_current; }
                if (b.c_flag) { s.CC_axis_offset = b.c_number; s.CC_current = s.CC_current; }

                Canon.SET_ORIGIN_OFFSETS(
                    s.origin_offset_x + s.axis_offset_x,
                    s.origin_offset_y + s.axis_offset_y,
                    s.origin_offset_z + s.axis_offset_z,
                    s.AA_origin_offset + s.AA_axis_offset,
                    s.BB_origin_offset + s.BB_origin_offset,
                    s.CC_origin_offset + s.CC_axis_offset,
                    s.UU_origin_offset + s.UU_axis_offset,
                    s.VV_origin_offset + s.VV_axis_offset
                );
            }
            else
            {
                throw new InvalidOperationException("NCE_BUG_CODE_NOT_G52_OR_G92");
            }

            return RS274NGC_OK;
        }

        // --- Canned‐cycle primitives (G81–G89) ---

        /// <summary>G81: simple drill. :contentReference[oaicite:3]{index=3}</summary>
        public static int ConvertCycleG81(CANON_PLANE plane, double X, double Y, double clearZ, double bottomZ)
        {
            CycleFeed(plane, X, Y, bottomZ);
            CycleTraverse(plane, X, Y, clearZ);
            return RS274NGC_OK;
        }

        /// <summary>G82: drill + dwell. :contentReference[oaicite:4]{index=4}</summary>
        private static int ConvertCycleG82(CANON_PLANE plane, double X, double Y, double clearZ, double bottomZ, double p)
        {
            CycleFeed(plane, X, Y, bottomZ);
            DWELL(p);
            CycleTraverse(plane, X, Y, clearZ);
            return RS274NGC_OK;
        }



        // -------------------------------------------------------------------------------------------------
        // ENHANCE_BLOCK  (C++: static int enhance_block) :contentReference[oaicite:7]{index=7}
        // -------------------------------------------------------------------------------------------------
        private static int EnhanceBlock(Block block, SetupData settings)
        {
            bool axisFlag =
                block.x_flag || block.y_flag ||
                block.z_flag || block.a_flag ||
                block.b_flag || block.c_flag ||
                block.u_flag || block.v_flag;

            int mode0 = block.g_modes[0];
            int mode1 = block.g_modes[1];
            bool modeZeroCovetsAxes =
                mode0 == G_10 ||
                mode0 == G_28 ||
                mode0 == G_30 ||
                mode0 == G_92 ||
                mode0 == G_92_3 ||
                mode0 == G_52;

            if (mode1 != -1)
            {
                if (mode1 == G_80)
                {
                    if (axisFlag && !modeZeroCovetsAxes)
                        return NCE_CANNOT_USE_AXIS_VALUES_WITH_G80;
                    if (!axisFlag && (mode0 == G_92 || mode0 == G_52))
                        return NCE_ALL_AXES_MISSING_WITH_G52_G92;
                }
                else
                {
                    if (modeZeroCovetsAxes)
                        return NCE_CANNOT_USE_TWO_G_CODES_THAT_BOTH_USE_AXIS_VALUES;
                    // note: original C++ commented-out the “all axes missing” for motion codes here :contentReference[oaicite:8]{index=8}
                }
                block.motion_to_be = mode1;
            }
            else if (modeZeroCovetsAxes)
            {
                if (!axisFlag && (mode0 == G_92 || mode0 == G_52))
                    return NCE_ALL_AXES_MISSING_WITH_G52_G92;
            }
            else if (
                axisFlag ||
                ((settings.motion_mode == G_2 || settings.motion_mode == G_3) &&
                 (block.i_flag || block.j_flag || block.k_flag))
            )
            {
                if (settings.motion_mode == -1 || settings.motion_mode == G_80)
                    return NCE_CANNOT_USE_AXIS_VALUES_WITHOUT_A_G_CODE_THAT_USES_THEM;
                block.motion_to_be = settings.motion_mode;
            }

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_ITEMS  (C++: static int check_items) :contentReference[oaicite:9]{index=9}
        // -------------------------------------------------------------------------------------------------
        private static int CheckItems(Block block, SetupData settings)
        {
            int status;
            if ((status = CheckGCodes(block, settings)) != RS274NGC_OK) return status;
            if ((status = CheckMCodes(block)) != RS274NGC_OK) return status;
            if ((status = CheckOtherCodes(block, settings)) != RS274NGC_OK) return status;
            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_M_CODES  (C++: static int check_m_codes) :contentReference[oaicite:10]{index=10}
        // -------------------------------------------------------------------------------------------------
        private static int CheckMCodes(Block block)
        {
            // 1. Too many M codes on one line
            if (block.m_count > MAX_EMS) 
                return NCE_TOO_MANY_M_CODES_ON_LINE;

            // 2. M98 loop parameters
            if (block.m_modes[4] == 98)  // mode 4 == M98
            {
                int pInt = (int)(block.p_number + 0.0001);
                if (block.p_number < 0.0)
                    return NCE_NEGATIVE_P_WORD_USED;
                if ((block.p_number + 0.0001 - pInt) > 0.0002)
                    return NCE_P_VALUE_NOT_AN_INTEGER_WITH_G10_L2_M98;

                // default Q=1 if neither Q nor L given
                if (!block.q_flag && !block.l_flag)
                {
                    block.q_number = 1.0;
                    block.q_flag = true;
                }

                int qInt = (int)(block.q_number + 0.0001);
                if ((block.q_number + 0.0001 - qInt) > 0.0002)
                    return NCE_Q_VALUE_NOT_AN_INTEGER_WITH_M98;

                int lInt = (int)(block.l_number + 0.0001);
                if ((block.l_number + 0.0001 - lInt) > 0.0002)
                    return NCE_L_VALUE_NOT_AN_INTEGER_WITH_M98;
            }

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CHECK_OTHER_CODES  (C++: static int check_other_codes) :contentReference[oaicite:11]{index=11}
        // -------------------------------------------------------------------------------------------------
        private static int CheckOtherCodes(Block block, SetupData _setup)
        {
            int motion = block.motion_to_be;

            // A, B, C, U, V not allowed in canned cycles (G81–G89)
            if (block.a_flag)
                if (block.g_modes[1] > G_80 && block.g_modes[1] < G_90)
                    return NCE_CANNOT_PUT_AN_A_IN_CANNED_CYCLE;
            if (block.b_flag)
                if (block.g_modes[1] > G_80 && block.g_modes[1] < G_90)
                    return NCE_CANNOT_PUT_A_B_IN_CANNED_CYCLE;
            if (block.c_flag)
                if (block.g_modes[1] > G_80 && block.g_modes[1] < G_90)
                    return NCE_CANNOT_PUT_A_C_IN_CANNED_CYCLE;
            if (block.u_flag)
                if (block.g_modes[1] > G_80 && block.g_modes[1] < G_90)
                    return NCE_CANNOT_PUT_A_U_IN_CANNED_CYCLE;
            if (block.v_flag)
                if (block.g_modes[1] > G_80 && block.g_modes[1] < G_90)
                    return NCE_CANNOT_PUT_A_V_IN_CANNED_CYCLE;

            // I, J, K only with arcs or G87
            if (block.i_flag && motion != G_2 && motion != G_3 && motion != G_87)
                return NCE_I_WORD_WITH_NO_G2_OR_G3_OR_G87_TO_USE_IT;
            if (block.j_flag && motion != G_2 && motion != G_3 && motion != G_87)
                return NCE_J_WORD_WITH_NO_G2_OR_G3_OR_G87_TO_USE_IT;
            if (block.k_flag && motion != G_2 && motion != G_3 && motion != G_87)
                return NCE_K_WORD_WITH_NO_G2_OR_G3_OR_G87_TO_USE_IT;

            // P only with G4, G10, G82, G83, G86, G88, G89, M98
            if (block.p_flag && !(
                block.g_modes[0] == G_4 || block.g_modes[0] == G_10 ||
                block.g_modes[1] == G_82 || block.g_modes[1] == G_83 ||
                block.g_modes[1] == G_86 || block.g_modes[1] == G_88 ||
                block.g_modes[1] == G_89 ||
                block.m_modes[4] == 98))
                return NCE_P_WORD_WITH_NO_G4_G10_G82_G86_G88_G89_M49_M98;

            // Q only with G83
            if (block.q_flag && block.g_modes[1] != G_83)
                return NCE_Q_WORD_MISSING_WITH_G83;

            // R only with G codes or M98/M100-119
            if (block.r_flag && !(
                block.g_modes[0] == G_10 || block.g_modes[1] == G_2 ||
                block.g_modes[1] == G_3 ||
                (block.g_modes[1] > G_80 && block.g_modes[1] < G_90) ||
                block.m_modes[4] == 98))
                return NCE_R_WORD_WITH_NO_G_CODE_THAT_USES_IT;

            return RS274NGC_OK;
        }

        // -------------------------------------------------------------------------------------------------
        // CYCLE_FEED / CYCLE_TRAVERSE  (C++: cycle_feed, cycle_traverse) 
        // -------------------------------------------------------------------------------------------------
        private static int CycleFeed(CANON_PLANE plane, double e1, double e2, double e3)
        {
            switch (plane)
            {
                case CANON_PLANE.XY:
                    Canon.STRAIGHT_FEED(e1, e2, e3, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
                case CANON_PLANE.YZ:
                    Canon.STRAIGHT_FEED(e3, e1, e2, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
                default: // XZ
                    Canon.STRAIGHT_FEED(e2, e3, e1, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
            }
            return RS274NGC_OK;
        }

        private static int CycleTraverse(CANON_PLANE plane, double e1, double e2, double e3)
        {
            switch (plane)
            {
                case CANON_PLANE.XY:
                    Canon.STRAIGHT_TRAVERSE(e1, e2, e3, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
                case CANON_PLANE.YZ:
                    Canon.STRAIGHT_TRAVERSE(e3, e1, e2, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
                default: // XZ
                    Canon.STRAIGHT_TRAVERSE(e2, e3, e1, _setup.AA_current, _setup.BB_current, _setup.CC_current, _setup.UU_current, _setup.VV_current);
                    break;
            }
            return RS274NGC_OK;
        }
        /// <summary>
        /// Top‐level G₂/G₃ converter. Computes arc parameters then emits the arc feed. :contentReference[oaicite:1]{index=1}</summary>
        private static int ConvertArc(int motion, Block b, SetupData s)
        {
            // compute arc: fe/final-end coords, se/start coords, fa/final-center coords, sa/start-center coords
            int status = ArcData(b.motion_to_be, b, s, out double fe, out double se, out double fa, out double sa, out int dir, out double ae);
            if (status != RS274NGC_OK) return status;
            // emit the feed‐arc command
            // ARC_FEED(x_start, y_start, z_start, a_start, b_start, c_start, u_start, v_start,
            //          x_end,   y_end,   z_end,   a_end,   b_end,   c_end,   u_end,   v_end,
            //          center_x_offset, center_y_offset, direction, angle);
       Canon.ARC_FEED((CANON_FEED_REFERENCE) s.feed_mode, fe, se, fa, sa, dir, s.current_z, s.AA_current, s.BB_current, s.CC_current, s.UU_current, s.VV_current);
            // update current position to arc end
            s.current_x = fe;
            s.current_y = se;
            // (leave Z,A,B,C,U,V unchanged for pure planar arcs)
            return RS274NGC_OK;
        }

        /// <summary>
        /// Compute the 2D arc parameters for G₂/G₃ in the active plane. :contentReference[oaicite:2]{index=2}</summary>
        private static int ArcData(int motion, Block b, SetupData s, out double fe, out double se, out double fa, out double sa, out int dir, out double ae)
        {
            // fe/se = end‐point in XY (or permuted)  
            fe = 0;
            se = 0;
            // fa/sa = center‐point in XY (or permuted)
            fa = 0;
            sa = 0;
            // dir   = CW/CCW flag
            dir = 0;
            // ae    = sweep angle (0…2π)
            ae = 0;

            int status;
            switch (s.plane)
            {
                case (int)CANON_PLANE.XY:
                    status = ArcDataCenter(s.current_x, s.current_y, b.x_number, b.y_number, b.i_flag ? b.i_number : 0, b.j_flag ? b.j_number : 0, out fe, out se, out fa, out sa, out dir, out ae);
                    break;
                case (int)CANON_PLANE.XZ:
                    status = ArcDataCenter(
                        s.current_x, s.current_z, b.x_number, b.z_number, b.i_flag ? b.i_number : 0, b.k_flag ? b.k_number : 0, out fe, out se, out fa, out sa, out dir, out ae);
                    break;
                case (int)CANON_PLANE.YZ:
                    status = ArcDataCenter(s.current_y, s.current_z, b.y_number, b.z_number, b.j_flag ? b.j_number : 0, b.k_flag ? b.k_number : 0, out fe, out se, out fa, out sa, out dir, out ae);
                    break;
                default:
                    return NCE_BUG_PLANE_NOT_XY_YZ_OR_XZ;
            }
            if (status != RS274NGC_OK) return status;

            // if G₃ (CCW), sweep = 2π - sweep computed for G₂
            if (motion == G_3)
                ae = (2.0 * Math.PI) - ae;

            return RS274NGC_OK;
        }

        /// <summary>
        /// Center‐based arc geometry: given start (x0,y0), end (x1,y1), and I/J or K offset,
        /// compute end‐point fe/se, center fa/sa, direction bit, and sweep angle ae. :contentReference[oaicite:3]{index=3}</summary>
        private static int ArcDataCenter(double x0, double y0, double x1, double y1, double iOffset, double jOffset, out double fe, out double se, out double fa, out double sa, out int dir, out double ae)
        {
            // center is start + offset
            fa = x0 + iOffset;
            sa = y0 + jOffset;
            double r = Math.Sqrt((x0 - fa) * (x0 - fa) + (y0 - sa) * (y0 - sa));
            
            // compute start/end angles
            double theta0 = Math.Atan2(y0 - sa, x0 - fa);
            double theta1 = Math.Atan2(y1 - sa, x1 - fa);
            ae = theta1 - theta0;

            // normalize sweep to [0,2π)
            if (ae < 0) ae += 2.0 * Math.PI;
            if (ae >= 2.0 * Math.PI) ae -= 2.0 * Math.PI;

            // direction bit: 0 = CW, 1 = CCW (matches ARC_FEED API)
            dir = (ae > 0) ? 1 : 0;

            // fe/se are simply the end‐point coordinates
            fe = x1;
            se = y1;
            // radius = distance center→start

            if (r < SIGMA) return NCE_ARC_RADIUS_TOO_SMALL_TO_REACH_END_POINT;

            return RS274NGC_OK;
        }
        /// <summary>G83: peck drilling. :contentReference[oaicite:5]{index=5}</summary>
        public static int ConvertCycleG83(CANON_PLANE plane, double X, double Y, double clearZ, double bottomZ, double P, double delta, CANON_DIRECTION direction, CANON_SPEED_FEED_MODE feedMode)
        {
            
            double r = clearZ;
            double rapidDelta = Math.Max(0.0, delta);
            double currentDepth = r - delta;
            while (currentDepth > bottomZ)
            {
                CycleFeed(plane, X, Y, currentDepth);
                DWELL(P);  // uses last P
                CycleTraverse(plane, X, Y, r);
                CycleTraverse(plane, X, Y, currentDepth + rapidDelta);
                if (_setup.CM.GetAbort()) return RS274NGC_EXIT;
                currentDepth -= delta;
            }
            CycleFeed(plane, X, Y, bottomZ);
            CycleTraverse(plane, X, Y, clearZ);
            return RS274NGC_OK;
        }

        /// <summary>G84–G89:</summary>
        private static int ConvertCycleG84(CANON_PLANE plane, double x, double y, double r, double clearZ, double bottomZ, CANON_DIRECTION direction, CANON_SPEED_FEED_MODE mode)
        {
            // spindle must be turning
            if (direction != CANON_DIRECTION.CANON_CLOCKWISE && direction != CANON_DIRECTION.CANON_COUNTERCLOCKWISE)
                return NCE_SPINDLE_NOT_TURNING_CLOCKWISE_IN_G84;

            // start synchronized speed/​feed if requested
            if (mode != CANON_SPEED_FEED_MODE.CANON_SYNCHED)
                Canon.START_SPEED_FEED_SYNCH();

            CycleFeed(plane, x, y, bottomZ);
            Canon.STOP_SPINDLE_TURNING();
            CycleTraverse(plane, x, y, clearZ);

            if (direction == CANON_DIRECTION.CANON_CLOCKWISE)
                Canon.START_SPINDLE_CLOCKWISE();
            else
                Canon.START_SPINDLE_COUNTERCLOCKWISE();

            if (mode != CANON_SPEED_FEED_MODE.CANON_SYNCHED)
                Canon.STOP_SPEED_FEED_SYNCH();

            Canon.STOP_SPINDLE_TURNING();
            Canon.START_SPINDLE_CLOCKWISE();

            return RS274NGC_OK;
        }

        // convert_cycle_g85  — G85 (boring/​reaming) :contentReference[oaicite:11]{index=11}
        private static int ConvertCycleG85(CANON_PLANE plane, double x, double y, double r, double clearZ, double bottomZ)     // bottom of hole
        {
            CycleFeed(plane, x, y, bottomZ);
            CycleFeed(plane, x, y, r);
            CycleTraverse(plane, x, y, clearZ);
            return RS274NGC_OK;
        }

        // convert_cycle_g86  — G86 (boring with dwell then retract and restart) :contentReference[oaicite:12]{index=12}
        private static int ConvertCycleG86(
            CANON_PLANE plane,
            double x, double y,
            double clearZ,      // clearance plane
            double bottomZ,     // bottom of hole
            double dwell,       // dwell time
            CANON_DIRECTION direction)
        {
            if (direction != CANON_DIRECTION.CANON_CLOCKWISE &&
                direction != CANON_DIRECTION.CANON_COUNTERCLOCKWISE)
                return NCE_SPINDLE_NOT_TURNING_IN_G86;

            CycleFeed(plane, x, y, bottomZ);
            DWELL(dwell);
            Canon.STOP_SPINDLE_TURNING();
            CycleTraverse(plane, x, y, clearZ);

            if (direction == CANON_DIRECTION.CANON_CLOCKWISE)
                Canon.START_SPINDLE_CLOCKWISE();
            else
                Canon.START_SPINDLE_COUNTERCLOCKWISE();

            return RS274NGC_OK;
        }

        // convert_cycle_g87  — G87 (back-boring) :contentReference[oaicite:13]{index=13}
        private static int ConvertCycleG87(CANON_PLANE plane, double x, double offsetX, double y, double offsetY, double r, double clearZ, double middleZ, double bottomZ, CANON_DIRECTION direction)
        {
            CycleTraverse(plane, offsetX, offsetY, r);
            Canon.STOP_SPINDLE_TURNING();
            Canon.ORIENT_SPINDLE(0.0, direction);
            CycleTraverse(plane, offsetX, offsetY, bottomZ);
            CycleTraverse(plane, offsetX, offsetY, clearZ);
            CycleTraverse(plane, x, y, clearZ);

            if (direction == CANON_DIRECTION.CANON_CLOCKWISE)
                Canon.START_SPINDLE_CLOCKWISE();
            else
                Canon.START_SPINDLE_COUNTERCLOCKWISE();

            return RS274NGC_OK;
        }

        // convert_cycle_g88  — G88 (boring with program stop) :contentReference[oaicite:14]{index=14}
        private static int ConvertCycleG88(CANON_PLANE plane, double x, double y, double bottomZ, double dwell, CANON_DIRECTION direction)
        {
            if (direction != CANON_DIRECTION.CANON_CLOCKWISE && direction != CANON_DIRECTION.CANON_COUNTERCLOCKWISE)
                return NCE_SPINDLE_NOT_TURNING_IN_G88;

            CycleFeed(plane, x, y, bottomZ);
            DWELL(dwell);
            Canon.STOP_SPINDLE_TURNING();
            ProgramStop();

            if (direction == CANON_DIRECTION.CANON_CLOCKWISE)
                Canon.START_SPINDLE_CLOCKWISE();
            else
                Canon.START_SPINDLE_COUNTERCLOCKWISE();

            return RS274NGC_OK;
        }

        // convert_cycle_g89  — G89 (boring with dwell then feed-retract) :contentReference[oaicite:15]{index=15}
        private static int ConvertCycleG89(
            CANON_PLANE plane,
            double x, double y,
            double clearZ,      // clearance plane
            double bottomZ,     // bottom of hole
            double dwell)       // dwell time
        {
            CycleFeed(plane, x, y, bottomZ);
            DWELL(dwell);
            CycleFeed(plane, x, y, clearZ);
            return RS274NGC_OK;
        }

        // === Plane‐specific wrappers ===

        // convert_cycle_yz  — dispatch G81–G89 in YZ plane :contentReference[oaicite:16]{index=16}
        private static int ConvertCycleYZ(int motion, Block block, SetupData settings)
        {
            // Resolve endpoints & depths exactly as in XY, but permuted for YZ...
            // (Identify old_cc, r, cc, clear_cc, aa, bb same as C++.)
            double aa       = block.a_number;
            double bb       = block.b_number;
            double clear_cc = block.clear_z;      // “clear plane” in the YZ-cycle
            double cc       = block.bottom_z;     // “bottom” in the YZ-cycle
            double r        = block.r_number;     // for peck-drilling if you need it
            var direction = (CANON_DIRECTION)settings.spindle_turning;
            var feedMode  = (CANON_SPEED_FEED_MODE)settings.speed_feed_mode;
            // Ensure exact-path for the cycle
            var saveMode = GET_EXTERNAL_MOTION_MODE();
            if (saveMode != (int)CANON_MOTION_MODE.CANON_EXACT_PATH)
                SetMotionControlMode((int)CANON_MOTION_MODE.CANON_EXACT_PATH);

            int status;
            switch (motion)
            {
                case G_81:
                    status = ConvertCycleG81(CANON_PLANE.YZ, aa, bb, clear_cc, cc);
                    break;
                case G_82:
                    status = ConvertCycleG82(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number);
                    break;
                case G_83:
                    status = ConvertCycleG83(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.q_number, r, direction, feedMode);
                    break;
                case G_84:
                    status = ConvertCycleG84(CANON_PLANE.YZ, aa, bb, r, clear_cc, cc, direction, feedMode);
                    break;
                case G_85:
                    status = ConvertCycleG85(CANON_PLANE.YZ, aa, bb, r, clear_cc, cc);
                    break;
                case G_86:
                    status = ConvertCycleG86(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number, direction);
                    break;
                case G_87:
                    status = ConvertCycleG87(CANON_PLANE.YZ, aa, aa + block.j_number, bb, bb + block.k_number, r, clear_cc, block.i_number, cc, direction);
                    break;
                case G_88:
                    status = ConvertCycleG88(CANON_PLANE.YZ, aa, bb, cc, block.p_number, direction);
                    break;
                case G_89:
                    status = ConvertCycleG89(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number);
                    break;
                default:
                    return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            }

            // Restore motion-control mode
            if (saveMode != (int)CANON_MOTION_MODE.CANON_EXACT_PATH)
                SetMotionControlMode(saveMode);

            return status;
        }


        // convert_cycle_zx  — dispatch G81–G89 in XZ plane :contentReference[oaicite:17]{index=17}
        private static int ConvertCycleZX(int motion, Block block, SetupData settings)
        {
            // Resolve endpoints & depths permuted for XZ...
            double aa       = block.a_number;
            double bb       = block.b_number;
            double clear_cc = block.clear_z;      // “clear plane” in the YZ-cycle
            double cc       = block.bottom_z;     // “bottom” in the YZ-cycle
            double r        = block.r_number;     // for peck-drilling if you need it
            var direction = (CANON_DIRECTION)settings.spindle_turning;
            var feedMode  = (CANON_SPEED_FEED_MODE)settings.speed_feed_mode;
            var saveMode = GET_EXTERNAL_MOTION_MODE();
            if (saveMode != (int)CANON_MOTION_MODE.CANON_EXACT_PATH) SetMotionControlMode((int)CANON_MOTION_MODE.CANON_EXACT_PATH);

            int status;
            switch (motion)
            {
                case G_81:
                    status = ConvertCycleG81(CANON_PLANE.YZ, aa, bb, clear_cc, cc);
                    break;
                case G_82:
                    status = ConvertCycleG82(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number);
                    break;
                case G_83:
                    status = ConvertCycleG83(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.q_number, r, direction, feedMode);
                    break;
                case G_84:
                    status = ConvertCycleG84(CANON_PLANE.YZ, aa, bb, r, clear_cc, cc, direction, feedMode);
                    break;
                case G_85:
                    status = ConvertCycleG85(CANON_PLANE.YZ, aa, bb, r, clear_cc, cc);
                    break;
                case G_86:
                    status = ConvertCycleG86(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number, direction);
                    break;
                case G_87:
                    status = ConvertCycleG87(CANON_PLANE.YZ, aa, aa + block.j_number, bb, bb + block.k_number, r, clear_cc, block.i_number, cc, direction);
                    break;
                case G_88:
                    status = ConvertCycleG88(CANON_PLANE.YZ, aa, bb, cc, block.p_number, direction);
                    break;
                case G_89:
                    status = ConvertCycleG89(CANON_PLANE.YZ, aa, bb, clear_cc, cc, block.p_number);
                    break;
                default:
                    return NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED;
            }

            if (saveMode != (int)CANON_MOTION_MODE.CANON_EXACT_PATH)
                SetMotionControlMode(saveMode);

            return status;
        }




        /// <summary>
        /// C# port of:
        /// static int read_comment(char *line, int *counter, block_pointer block, double *parameters)
        /// </summary>
        public static int ReadComment(string line, ref int counter, Block block, double[] parameters)
        {
            const string name = "read_comment";
            int n;
            int status;

            // CHK((line[*counter] != '('), NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED);
            if ((status = CHK(line[counter] != '(', NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED, name)) != 0)
            {
                return status;
            }
            // (*counter)++;
            counter++;

            // find end of any previous data
            for (n = 0; n < MaxGComment - 4 && block.comment[n] != '\0'; n++)
            { }

            // CHK((n == MAX_G_COMMENT-4), NCE_UNCLOSED_COMMENT_FOUND);
            if ((status = RS274NGC.CHK(n == MaxGComment - 4, NCE_UNCLOSED_COMMENT_FOUND, name)) != 0)
            {
                return status;
            }

            // block->comment[n++] = '(';
            block.comment[n++] = '(';

            // for (; line[*counter] != ')' && n<MAX_G_COMMENT-4; (*counter)++, n++)
            for (; line[counter] != ')' && n < MaxGComment - 4; counter++, n++)
            {
                block.comment[n] = line[counter];
            }

            // CHK((n == MAX_G_COMMENT-4), NCE_UNCLOSED_COMMENT_FOUND);
            if ((status = CHK(n == MaxGComment - 4, NCE_UNCLOSED_COMMENT_FOUND, name)) != 0)
            {
                return status;
            }

            // block->comment[n++] = ')';
            // block->comment[n]   = 0;
            block.comment[n++] = ')';
            block.comment[n] = '\0';

            // (*counter)++;
            counter++;

            // return RS274NGC_OK;
            return RS274NGC_OK;
        }

        public static int ReadParameter(string line, ref int counter, out double value, double[] parameters)
        {
            const string name = "read_parameter";
            int index = 0;
            int status;
            value = 0;
            // CHK((line[*counter] != '#'), NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED);
            if ((status = CHK(line[counter] != '#', NCE_BUG_FUNCTION_SHOULD_NOT_HAVE_BEEN_CALLED, name)) != 0)
            {
                value = default;
                return status;
            }

            // *counter = (*counter + 1);
            counter++;

            // CHP(read_integer_value(line, counter, &index, parameters));
            // read the integer value straight into index
            status = ReadIntegerValue(line, ref counter, out index, parameters);
            if (status != RS274NGC_OK) return ERM(status, name);


            // CHK((index < 1) || (index >= RS274NGC_MAX_PARAMETERS), NCE_PARAMETER_NUMBER_OUT_OF_RANGE);
            if ((status = CHK(index < 1 || index >= RS274NGC_MAX_PARAMETERS, NCE_PARAMETER_NUMBER_OUT_OF_RANGE, name)) != 0)
            {
                value = default;
                return status;
            }

            // *double_ptr = parameters[index];
            value = parameters[index];

            // return RS274NGC_OK;
            return RS274NGC_OK;
        }

        public static int CheckGCodes(Block block, SetupData settings)
        {
            const string name = "check_g_codes";

            int mode0 = block.g_modes[0];
            int status, pInt;

            // MODE = none
            if (mode0 == -1)
            {
                return RS274NGC_OK;
            }
            // G4: must have P word ≥0 and dwell‐flag on
            else if (mode0 == G_4)
            {
                if ((status = CHK(!block.p_flag,
                                            NCE_DWELL_TIME_MISSING_WITH_G4,
                                            name)) != 0)
                    return status;

                if ((status = CHK(block.p_number < 0,
                                            NCE_NEGATIVE_P_WORD_USED,
                                            name)) != 0)
                    return status;

                return RS274NGC_OK;
            }
            // G10: must have L2, integer P in [1..9]
            else if (mode0 == G_10)
            {
                // round‐toward‐zero test
                pInt = (int)(block.p_number + 0.0001);

                if ((status = CHK(block.l_number != 2,
                                            NCE_LINE_WITH_G10_DOES_NOT_HAVE_L2,
                                            name)) != 0)
                    return status;

                if ((status = CHK(
                        (block.p_number + 0.0001 - pInt) > 0.0002,
                        NCE_P_VALUE_NOT_AN_INTEGER_WITH_G10_L2_M98,
                        name)) != 0)
                    return status;

                if ((status = CHK(pInt < 1 || pInt > 9,
                                            NCE_P_VALUE_OUT_OF_RANGE_WITH_G10_L2,
                                            name)) != 0)
                    return status;

                return RS274NGC_OK;
            }
            // G28, G30, G53, G52, G92, G92.1, G92.2, G92.3 all fall through their own checks:
            else if (mode0 == G_28 ||
                    mode0 == G_30)
            {
                return RS274NGC_OK;
            }
            else if (mode0 == G_53)
            {
                if ((status = CHK(
                        block.motion_to_be != G_0 && block.motion_to_be != G_1,
                        NCE_MUST_USE_G0_OR_G1_WITH_G53,
                        name)) != 0)
                    return status;

                if ((status = CHK(
                        (block.g_modes[3] == G_91) ||
                        (block.g_modes[3] != G_90 && settings.distance_mode == (int)RS274NGC_DISTANCE_MODE.MODE_INCREMENTAL),
                        NCE_CANNOT_USE_G53_INCREMENTAL,
                        name)) != 0)
                    return status;

                return RS274NGC_OK;
            }
            else if (mode0 == G_52 ||
                    mode0 == G_92 ||
                    mode0 == G_92_1 ||
                    mode0 == G_92_2 ||
                    mode0 == G_92_3)
            {
                return RS274NGC_OK;
            }

            // anything else = modal‐group‐0 error
            return ERM(NCE_BUG_BAD_G_CODE_MODAL_GROUP_0, name);
        }
        public static int convert_dwell(double time)
        {				/* time in seconds to dwell */
            DWELL(time);
            return RS274NGC_OK;
        }
        // <summary>
        /// Handles any parenthetical comment that isn’t an “MSG …” directive.
        /// </summary>
        public static void Comment(string text)
        {
            // TODO: wire this into your UI or log system
            Console.WriteLine($"COMMENT: {text}");
        }

        /// <summary>
        /// Handles an “MSG …” directive inside parentheses.
        /// </summary>
        public static void Message(string text)
        {
            // TODO: wire this into your UI or log system
            Console.WriteLine($"MESSAGE: {text}");
        }


        public static int ConvertCycleG76(int a, double b, double c, double d, double e, double f) => throw new NotImplementedException();
        public static int CheckDoneBuf() => throw new NotImplementedException();
        public static int GetAxisDone(int i, out int r) => throw new NotImplementedException();
        public static int GetAbsPositionRelative(ref double x, ref double y) => throw new NotImplementedException();
        public static int MeasurePointAppendToFile(string filename) => throw new NotImplementedException();
        public static int DoSpecialInitialCommands() => throw new NotImplementedException();
        public static int DoSpecialCommands() => throw new NotImplementedException();
        public static int LaunchCoordMotion() => throw new NotImplementedException();
        public static int SetRapidSettings(int axis, double val) => throw new NotImplementedException();
        public static int GetRapidSettings() => throw new NotImplementedException();
        public static int DWELL(double time) => throw new NotImplementedException();
        public static int SetSpindleMode() => throw new NotImplementedException();
        public static int SetMotionControlMode(int a) => throw new NotImplementedException();
        public static int DoSpecialCommand(int seg) => throw new NotImplementedException();
        public static int GetRapidSettingsAxis(double axis, out double vel, out double accel, out double decel, out double jerk, out double softPos, out double softNeg, out double countsPerInch, out string axisName) => throw new NotImplementedException();
        public static int ReadCurAbsPositionFull(out double x, out double y, out double z, out double u, out double v, bool snap, bool noGeo) => throw new NotImplementedException();
        public static int ReadCurAbsPosition(out double x, out double y, out double z, bool snap, bool noGeo) => throw new NotImplementedException();
        public static void GET_EXTERNAL_PARAMETER_FILE_NAME(char[] buf, int max) => throw new NotImplementedException();
        public static void USE_LENGTH_UNITS(int u) => throw new NotImplementedException();
        public static int CHECK_INIT_ON_EXEC() => throw new NotImplementedException();
        public static int EnableFeedOverride() => throw new NotImplementedException();
        public static int DisableFeedOverride() => throw new NotImplementedException();
        public static int EnableSpeedOverride() => throw new NotImplementedException();
        public static int DisableSpeedOverride() => throw new NotImplementedException();
        public static int StraightTraverse() => throw new NotImplementedException();
        public static int GET_EXTERNAL_MOTION_MODE() => throw new NotImplementedException();
        public static int ProgramStop() => throw new NotImplementedException();
        public static int ConvertToolLengthOffset(int a, Block b, SetupData s) => throw new NotImplementedException();
        public static int Convertdistance_mode(int a, SetupData s) => throw new NotImplementedException();
        public static void MistOn() { /* TODO: GPIO or UI hook */ }
        public static void MistOff() { /* TODO */ }
        public static void FloodOn() { /* TODO */ }
        public static void FloodOff() { /* TODO */ }
        public static void TurnProbeOn() { /* TODO */ }
        public static void TurnProbeOff() { /* TODO */ }
        public static void CoolantState(bool on)
        {
            if (on) FloodOn();
            else FloodOff();
        }
        public static void M100(int code)
        {
            // TODO: hook this up to your actual coolant/mist hardware or UI.
            // For now, just turn everything off:
            MistOff();
            FloodOff();
        }
        /// <summary>Perform a straight‐line probe toward the part.</summary>
        public static int StraightProbe(double x, double y, double z, double a, double b, double c, double u, double v)
        {
            // TODO: drive the axes toward the part until the probe trips.
            return RS274NGC_OK;
        }

        public static void PChanged(string parmName)
        {
            // TODO: wire this into your UI or logger.
            // For now it just writes to the console:
            Console.WriteLine($"[RS274NGC] Parameter changed: {parmName}");
        }

        public static int LookupToolIndex(SetupData s, int t)
        {
            // For now just return t unchanged;
            // you can replace this with real lookup logic later.
            return t;
        }
       
        /// <summary>
        /// Stub for the plane‐selection helper (G17/G18/G19).
        /// In the real interpreter this would swap the axis ordering,
        /// set flags, etc.  Here it’s a no-op or you can raise an event.
        /// </summary>

    }
    
}

