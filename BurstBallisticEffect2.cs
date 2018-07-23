//namespace BurstBallisticAmmoChecker
//{
//    public class BurstBallisticEffect2 : BurstBallisticEffect
//    {
//        private float floatieInterval;
//        private float nextFloatie;
//
//        protected override void Update()
//        {
//            base.Update();
//            if (this.currentState != WeaponEffect.WeaponEffectState.Firing)
//                return;
//            if ((double) this.t >= (double) this.impactTime && 
//                (double) this.t >= (double) this.nextFloatie &&
//                hitIndex < this.hitInfo.hitLocations.Length && 
//                this.hitInfo.hitLocations[this.hitIndex] != 0 &&
//                this.hitInfo.hitLocations[this.hitIndex] != 65536)
//            {
//                this.nextFloatie = this.t + this.floatieInterval;
//                this.PlayImpact();
//            }
//
//            if ((double) this.t < 1.0)
//                return;
//            float hitDamage = this.weapon.DamagePerShotAdjusted(this.weapon.parent.occupiedDesignMask);
//            for (int index = 0; index < this.hitInfo.hitLocations.Length && index < this.weapon.ShotsWhenFired; ++index)
//            {
//                if (this.hitInfo.hitLocations[index] != 0 && this.hitInfo.hitLocations[index] != 65536)
//                {
//                    this.hitIndex = index;
//                    this.OnImpact(hitDamage);
//                }
//            }
//
//            this.OnComplete();
//        }
//    }
//}