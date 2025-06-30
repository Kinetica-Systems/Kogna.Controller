using System;
using KinematicEngine;

public class Kinematics6AxisFanuc : CKinematics 
{
    // Link length constants
    private const double L1_X = 180.0;    // mm
    private const double L1_Z = 1000.0;   // mm
    private const double L2   = 950.0;    // mm
    private static readonly double L3 = Math.Sqrt(1150*1150 +  240*240);
    private const double L6   = 200.0;    // mm

    public Kinematics6AxisFanuc() { }

    // --------- INVERSE KINEMATICS: Cartesian [X,Y,Z,A,B,C] -> Actuators [J1..J6] ----------
    public double[] TransformCADtoActuators(double[] cartesian)
    {
        // Expects [x, y, z, a, b, c]
        double[] actuators = new double[6];
        if (!SolveInverseKinematicsFanuc(cartesian, actuators))
        {
            // If direct IK fails, fallback to numerical (not robust, but exists)
            InvertTransformCADtoActuators(cartesian, actuators);
        }
        return actuators;
    }

    // --------- FORWARD KINEMATICS: Actuators [J1..J6] -> Cartesian [X,Y,Z,A,B,C] ----------
    public double[] TransformActuatorsToCAD(double[] actuators)
    {
        double[] cartesian = new double[6];
        // First 3 axes FK (position)
        double[] pos = ForwardKinematicsFanuc(
            actuators[0], // theta1 (deg)
            actuators[1], // theta2 (deg)
            actuators[2]  // theta3 (deg)
        );
        cartesian[0] = pos[0]; // X
        cartesian[1] = pos[1]; // Y
        cartesian[2] = pos[2]; // Z

        // Pass-through for A, B, C
        cartesian[3] = actuators.Length > 3 ? actuators[3] : 0.0;
        cartesian[4] = actuators.Length > 4 ? actuators[4] : 0.0;
        cartesian[5] = actuators.Length > 5 ? actuators[5] : 0.0;

        return cartesian;
    }

    // --------- LOW-LEVEL FANUC IK, expects [x, y, z, a, b, c] in and [j1..j6] out ----------
    private static bool SolveInverseKinematicsFanuc(double[] cart, double[] acts)
    {
        double x = cart[0], y = cart[1], z = cart[2];
        double a = cart[3], b = cart[4], c = cart[5];

        double ar = DegToRad(a), br = DegToRad(b), cr = DegToRad(c);

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

        // Set remaining joints to zero for now
        acts[0] = RadToDeg(theta1);
        acts[1] = RadToDeg(theta2);
        acts[2] = RadToDeg(theta3);
        acts[3] = 0;
        acts[4] = 0;
        acts[5] = 0;
        return true;
    }

    // --------- NUMERICAL IK FALLBACK ----------
    // Tries to find a joint solution for cartesian input, returns actuators in 'acts'
    private void InvertTransformCADtoActuators(double[] cart, double[] acts)
    {
        const double Tol = 1e-6;
        const double d = 0.1;
        double x = cart[0], y = cart[1], z = cart[2];
        double aRot = cart[3], bRot = cart[4], cRot = cart[5];
        const int NVAR = 6;

        double[] Acts0 = new double[6];
        double[] ActsX = new double[6];
        double[] ActsY = new double[6];
        double[] ActsZ = new double[6];
        double[] ActsA = new double[6];
        double[] ActsB = new double[6];
        double[] ActsC = new double[6];
        double[] A = new double[NVAR * (NVAR + 1)];

        for (int iter = 0; iter < 100; iter++)
        {
            SolveInverseKinematicsFanuc(new[] { x,      y,      z,      aRot, bRot, cRot }, Acts0);
            SolveInverseKinematicsFanuc(new[] { x + d,  y,      z,      aRot, bRot, cRot }, ActsX);
            SolveInverseKinematicsFanuc(new[] { x,      y + d,  z,      aRot, bRot, cRot }, ActsY);
            SolveInverseKinematicsFanuc(new[] { x,      y,      z + d,  aRot, bRot, cRot }, ActsZ);
            SolveInverseKinematicsFanuc(new[] { x,      y,      z,      aRot + d, bRot, cRot }, ActsA);
            SolveInverseKinematicsFanuc(new[] { x,      y,      z,      aRot, bRot + d, cRot }, ActsB);
            SolveInverseKinematicsFanuc(new[] { x,      y,      z,      aRot, bRot, cRot + d }, ActsC);

            // Build Jacobian matrix + error vector
            for (int j = 0; j < NVAR; j++)
            {
                int idx = j * (NVAR + 1);
                A[idx + 0] = (ActsX[j] - Acts0[j]) / d;
                A[idx + 1] = (ActsY[j] - Acts0[j]) / d;
                A[idx + 2] = (ActsZ[j] - Acts0[j]) / d;
                A[idx + 3] = (ActsA[j] - Acts0[j]) / d;
                A[idx + 4] = (ActsB[j] - Acts0[j]) / d;
                A[idx + 5] = (ActsC[j] - Acts0[j]) / d;
                A[idx + 6] = acts[j] - Acts0[j];
            }

            // Solve linear system A for [dx,dy,dz,da,db,dc] (implemented in base class)
            Solve(A, NVAR);

            // Extract deltas
            double ex = A[0 * (NVAR + 1) + NVAR];
            double ey = A[1 * (NVAR + 1) + NVAR];
            double ez = A[2 * (NVAR + 1) + NVAR];
            double ea = A[3 * (NVAR + 1) + NVAR];
            double eb = A[4 * (NVAR + 1) + NVAR];
            double ec = A[5 * (NVAR + 1) + NVAR];

            // Check convergence
            if (Math.Abs(ex) < Tol && Math.Abs(ey) < Tol && Math.Abs(ez) < Tol &&
                Math.Abs(ea) < Tol && Math.Abs(eb) < Tol && Math.Abs(ec) < Tol)
            {
                acts[0] = x; acts[1] = y; acts[2] = z;
                acts[3] = aRot; acts[4] = bRot; acts[5] = cRot;
                return;
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
        acts[0] = x; acts[1] = y; acts[2] = z;
        acts[3] = aRot; acts[4] = bRot; acts[5] = cRot;
    }

    // --------- FANUC FORWARD KINEMATICS: [j1,j2,j3] (deg) → [x,y,z] ----------
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

    // --------- UTILITY: 3D ROTATION (unused by main FK/IK) ----------
    public void Rotate3(double xc, double yc, double zc, double x, double y, double z, double a, double b, double c, out double xp, out double yp, out double zp)
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

    // --------- DEG/RAD UTILS ----------
    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;
}
