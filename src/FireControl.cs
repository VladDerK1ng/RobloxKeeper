using System;

namespace RobloxKeeper
{
    // When a watcher is allowed to speak.
    //
    // Three separate jobs, all of them about not being a nuisance:
    //
    //   Edge, not level. A leaderboard is on screen permanently. Reporting
    //   every scan that matches would be forty messages in ten seconds, so a
    //   watcher fires on the moment something ARRIVES.
    //
    //   Confirmation. OCR flickers and a single bad frame is not news, so a
    //   sighting has to hold for a few scans running.
    //
    //   Cooldown. Something that blinks in and out must not be reported every
    //   time it blinks.
    class FireControl
    {
        readonly int confirmScans;
        readonly TimeSpan cooldown;

        int seenRun;        // consecutive scans with it present
        int goneRun;        // consecutive scans with it absent
        bool fired;         // has fired and not yet rearmed
        DateTime lastFired = DateTime.MinValue;

        public FireControl(int confirmScans, TimeSpan cooldown)
        {
            // Zero would mean a watcher that can never fire, which is never
            // what anyone meant by it.
            this.confirmScans = confirmScans < 1 ? 1 : confirmScans;
            this.cooldown = cooldown;
        }

        public bool IsArmed { get { return !fired; } }

        // Feed one scan in; true only on the scan that should send something.
        public bool Update(bool seenNow, DateTime now)
        {
            if (!seenNow)
            {
                seenRun = 0;
                goneRun++;
                // Two absent scans means it really has gone, rather than one
                // frame being misread. Only then is it allowed to be news
                // again if it comes back.
                if (goneRun >= 2) fired = false;
                return false;
            }

            goneRun = 0;
            seenRun++;

            if (fired) return false;
            if (seenRun < confirmScans) return false;

            // MinValue means nothing has fired yet, so there is no cooldown to
            // serve - a watcher must not be silent on its first sighting.
            if (lastFired != DateTime.MinValue && now - lastFired < cooldown) return false;

            fired = true;
            lastFired = now;
            return true;
        }
    }
}
