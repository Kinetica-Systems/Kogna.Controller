using System;
using System.Collections.Generic;
using KinematicEngine;
// Ported from Kinematics6AxisFanuc.h/cpp
public class Kinematics6AxisFanuc : CKinematics
{
    // Link length constants
    private const double L1_X = 0.180;
    private const double L1_Z = 1.000;
    private const double L2 = 0.950;
    private static readonly double L3 = Math.Sqrt(1.150 * 1.150 + 0.240 * 0.240);
    private const double L6 = 0.200;

    // Pivot offset
    public double PivotToChuckLength { get; set; }

    public Kinematics6AxisFanuc()
    {
        PivotToChuckLength = 7.874;
        // Initialize motion parameters (inherited from CKinematics)
        m_MotionParams.MaxLinearLength = 0.05;
        m_MotionParams.MaxAngularChange = 0.5;
        m_MotionParams.MaxRapidFRO = 1.0;
        m_MotionParams.UseOnlyLinearSegments = true;
        m_MotionParams.DoRapidsAsFeeds = true;
        
    }
    public virtual int TransformCADtoActuators(double x, double y, double z, double a, double b, double c, double u, double v, double[] Acts, bool NoGeo = false)
    {
        // 1) call the existing 6-axis version
        int rc = TransformCADtoActuators(x, y, z, a, b, c, Acts, NoGeo);

        // 2) shove U/V straight into slots 6/7
        if (Acts.Length > 6) Acts[6] = u;
        if (Acts.Length > 7) Acts[7] = v;

        return rc;
    }

    // Transform CAD coordinates (X,Y,Z,A,B,C) to actuator angles
    public int TransformCADtoActuators(double x, double y, double z, double a, double b, double c, double[] Acts, bool NoGeo = false)
    {
        if (!SolveInverseKinematicsFanuc(x, y, z, a, b, c, Acts))
        {
            return InvertTransformCADtoActuators(
                Acts,
                out x, out y, out z,
                out a, out b, out c,
                NoGeo);
        }
        return 0;
    }
    public int TransformActuatorstoCAD(double[] Acts, out double xr, out double yr, out double zr, out double ar, out double br, out double cr, out double ur, out double vr, bool NoGeo = false)
    {
        // first do the existing 6-axis solve
        TransformActuatorstoCAD(Acts, out xr, out yr, out zr, out ar, out br, out cr, NoGeo);

        // then pass-through (or table-map) the last two actuators:
        ur = (Acts.Length > 6) ? Acts[6] : 0.0;
        vr = (Acts.Length > 7) ? Acts[7] : 0.0;

        return 0;
    }
    // Transform actuator angles back to CAD coordinates
    public  int TransformActuatorstoCAD(double[] Acts, out double xr, out double yr, out double zr, out double ar, out double br, out double cr, bool NoGeo = false)
    {
        return InvertTransformCADtoActuators(Acts, out xr, out yr, out zr, out ar, out br, out cr, NoGeo);
    }

    // Numerical inverse transform if direct IK fails
    public int InvertTransformCADtoActuators(double[] Acts, out double xr, out double yr, out double zr, out double ar, out double br, out double cr, bool NoGeo = false)
    {
        const double Tol = 1e-6;
        const double d = 0.1;
        double x = 0, y = 0, z = 5;
        double aRot = 0, bRot = 0, cRot = 0;
        int n = Acts.Length;

        // Arrays for finite differences
        double[] Acts0 = new double[n];
        double[] ActsX = new double[n];
        double[] ActsY = new double[n];
        double[] ActsZ = new double[n];
        double[] ActsA = new double[n];
        double[] ActsB = new double[n];
        double[] ActsC = new double[n];
        double[] A = new double[n * 7];

        for (int iter = 0; iter < 100; iter++)
        {
            TransformCADtoActuators(x, y, z, aRot, bRot, cRot, Acts0, NoGeo);
            TransformCADtoActuators(x + d, y, z, aRot, bRot, cRot, ActsX, NoGeo);
            TransformCADtoActuators(x, y + d, z, aRot, bRot, cRot, ActsY, NoGeo);
            TransformCADtoActuators(x, y, z + d, aRot, bRot, cRot, ActsZ, NoGeo);
            TransformCADtoActuators(x, y, z, aRot + d, bRot, cRot, ActsA, NoGeo);
            TransformCADtoActuators(x, y, z, aRot, bRot + d, cRot, ActsB, NoGeo);
            TransformCADtoActuators(x, y, z, aRot, bRot, cRot + d, ActsC, NoGeo);

            // Build Jacobian matrix + error vector
            for (int j = 0; j < n; j++)
            {
                int idx = j * 7;
                A[idx + 0] = (ActsX[j] - Acts0[j]) / d;
                A[idx + 1] = (ActsY[j] - Acts0[j]) / d;
                A[idx + 2] = (ActsZ[j] - Acts0[j]) / d;
                A[idx + 3] = (ActsA[j] - Acts0[j]) / d;
                A[idx + 4] = (ActsB[j] - Acts0[j]) / d;
                A[idx + 5] = (ActsC[j] - Acts0[j]) / d;
                A[idx + 6] = Acts[j]    - Acts0[j];
            }

            // Solve linear system A for [dx,dy,dz,da,db,dc] (implemented in base class)
            Solve(A, n);

            // Extract deltas
            double ex = A[0 * 7 + 6];
            double ey = A[1 * 7 + 6];
            double ez = A[2 * 7 + 6];
            double ea = A[3 * 7 + 6];
            double eb = A[4 * 7 + 6];
            double ec = A[5 * 7 + 6];

            // Check convergence
            if (Math.Abs(ex) < Tol && Math.Abs(ey) < Tol && Math.Abs(ez) < Tol &&
                Math.Abs(ea) < Tol && Math.Abs(eb) < Tol && Math.Abs(ec) < Tol)
            {
                xr = x; yr = y; zr = z;
                ar = aRot; br = bRot; cr = cRot;
                return 0;
            }

            // Update guess
            x    += ex;
            y    += ey;
            z    += ez;
            aRot += ea;
            bRot += eb;
            cRot += ec;
        }

        // Return last estimate if not converged
        xr = x; yr = y; zr = z;
        ar = aRot; br = bRot; cr = cRot;
        return 1;
    }

    // Rotate a point around (xc,yc,zc) by angles a,b,c (degrees)
    public void Rotate3(
        double xc, double yc, double zc,
        double x,  double y,  double z,
        double a,  double b,  double c,
        out double xp, out double yp, out double zp)
    {
        double ar = DegToRad(a);
        double br = DegToRad(b);
        double cr = DegToRad(c);

        double xa = x;
        double ya = yc + (y - yc) * Math.Cos(ar) - (z - zc) * Math.Sin(ar);
        double za = zc + (y - yc) * Math.Sin(ar) + (z - zc) * Math.Cos(ar);

        double xb = xc + (xa - xc) * Math.Cos(br) - (za - zc) * Math.Sin(br);
        double yb = ya;
        double zb = zc + (xa - xc) * Math.Sin(br) + (za - zc) * Math.Cos(br);

        xp = xc + (xb - xc) * Math.Cos(cr) - (yb - yc) * Math.Sin(cr);
        yp = yc + (xb - xc) * Math.Sin(cr) + (yb - yc) * Math.Cos(cr);
        zp = zb;
    }

    // Direct inverse kinematics solution (fanuc 6-axis)
    private static bool SolveInverseKinematicsFanuc(
        double x, double y, double z,
        double a, double b, double c,
        double[] Acts)
    {
        double ar = DegToRad(a);
        double br = DegToRad(b);
        double cr = DegToRad(c);

        double ca = Math.Cos(ar), cb = Math.Cos(br), cc = Math.Cos(cr);
        double sa = Math.Sin(ar), sb = Math.Sin(br), sc = Math.Sin(cr);

        // Tool orientation axis
        double nx = cb * cc;
        double ny = cb * sc;
        double nz = -sb;

        // Wrist center
        double wx = x - L6 * nx - L1_X;
        double wy = y - L6 * ny;
        double wz = z - L6 * nz - L1_Z;

        // Planar joint 1
        double theta1 = Math.Atan2(wy, wx);
        double r = Math.Sqrt(wx * wx + wy * wy);
        double s = wz;

        // Joint 2/3 via law of cosines
        double D = (r*r + s*s - L2*L2 - L3*L3) / (2 * L2 * L3);
        if (D < -1 || D > 1) return false;

        double theta3 = Math.Atan2(Math.Sqrt(1 - D*D), D);
        double theta2 = Math.Atan2(s, r) - Math.Atan2(L3 * Math.Sin(theta3), L2 + L3 * Math.Cos(theta3));

        // Set remaining joints to zero
        Acts[0] = RadToDeg(theta1);
        Acts[1] = RadToDeg(theta2);
        Acts[2] = RadToDeg(theta3);
        Acts[3] = 0;
        Acts[4] = 0;
        Acts[5] = 0;
        return true;
    }

    // Forward kinematics for joints 1-3 only (returns X,Y,Z)
    public static double[] ForwardKinematicsFanuc(double theta1_deg, double theta2_deg, double theta3_deg)
    {
        double t1 = DegToRad(theta1_deg);
        double t2 = DegToRad(theta2_deg);
        double t3 = DegToRad(theta3_deg);

        double x = L2 * Math.Cos(t2) + L3 * Math.Cos(t2 + t3);
        double z = L2 * Math.Sin(t2) + L3 * Math.Sin(t2 + t3);

        double px = Math.Cos(t1) * x + L1_X;
        double py = Math.Sin(t1) * x;
        double pz = z + L1_Z + L6;

        return new double[] { px, py, pz };
    }

    // Utility conversions
    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;
}
