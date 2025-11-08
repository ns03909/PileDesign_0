using System;

namespace PileDesignCore.Pile
{
    internal class LoadCase
    {
        public bool Ap { get; set; }
        public int ID { get; set; }
        public string Name { get; set; }
        public float Direction { get; set; }
        public bool is_soil_nonlinear { get; set; }
        public bool is_pile_nonlinear { get; set; }
        public float EA_multiplier { get; set; }
        public int SeismicLevel { get; set; }
        public float UpperMassForce { get; set; }
        public float FoundationMassForce { get; set; }
        public float ShearCenterX { get; set; }
        public float ShearCenterY { get; set; }
        public float ShearCenterArmX { get; set; }
        public float ShearCenterArmY { get; set; }
        public float F_InertialForce_x { get; set; }
        public float F_InertialForce_y { get; set; }
        public float F_InertialForce_zz { get; set; }
        public float dF_InertialForce_x { get; set; }
        public float dF_InertialForce_y { get; set; }
        public float dF_InertialForce_zz { get; set; }

        public LoadCase(
            int ID,
            string application,
            string name,
            float direction,
            string is_soil_nonlinear,
            string is_pile_nonlinear,
            float EA_multiplier,
            int seismiclevel,
            float uppermassforce,
            float foundationmassforce,
            float shearcenter_x,
            float shearcenter_y
        )
        {
            Ap = bool.Parse(application);
            this.ID = ID;
            Name = name;
            Direction = direction;
            this.is_soil_nonlinear = bool.Parse(is_soil_nonlinear);
            this.is_pile_nonlinear = bool.Parse(is_pile_nonlinear);
            this.EA_multiplier = EA_multiplier;
            SeismicLevel = seismiclevel;
            UpperMassForce = uppermassforce;
            FoundationMassForce = foundationmassforce;
            ShearCenterX = shearcenter_x;
            ShearCenterY = shearcenter_y;
        }

        public void set_shearcenterarms(float X0, float Y0)
        {
            ShearCenterArmX = ShearCenterX - X0;
            ShearCenterArmY = ShearCenterY - Y0;
        }

        public void set_F_inertialForce()
        {
            F_InertialForce_x = 0.0f;
            F_InertialForce_y = 0.0f;
            F_InertialForce_zz = 0.0f;
        }

        public void set_dF_inertialForce(float dir, float beta1, float beta2, int nstep)
        {
            float dF_inertialForce = (UpperMassForce * beta1 + FoundationMassForce * beta2) / nstep;
            dF_InertialForce_x = dF_inertialForce * (float)Math.Cos(Math.PI * dir / 180.0);
            dF_InertialForce_y = dF_inertialForce * (float)Math.Sin(Math.PI * dir / 180.0);
            dF_InertialForce_zz = -dF_InertialForce_x * ShearCenterArmY + dF_InertialForce_y * ShearCenterArmX;
        }

        public void update_F_inertialForce()
        {
            F_InertialForce_x += dF_InertialForce_x;
            F_InertialForce_y += dF_InertialForce_y;
            F_InertialForce_zz += dF_InertialForce_zz;
        }
    }
}
