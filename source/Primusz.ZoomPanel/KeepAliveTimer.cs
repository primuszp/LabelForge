using System;
using System.Windows.Threading;

namespace Primusz.ZoomPanel
{
    public class KeepAliveTimer
    {
        private readonly DispatcherTimer timer;
        private DateTime startTime;
        private TimeSpan? runTime;

        public TimeSpan Time { get; set; }

        public Action Action { get; set; }

        public bool Running { get; private set; }

        public KeepAliveTimer(TimeSpan time, Action action)
        {
            Time = time;
            Action = action;
            timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = time };
            timer.Tick += TimerExpired;
        }

        private void TimerExpired(object sender, EventArgs e)
        {
            lock (timer)
            {
                Running = false;
                timer.Stop();
                runTime = DateTime.UtcNow.Subtract(startTime);
                Action();
            }
        }

        public void Nudge()
        {
            lock (timer)
            {
                if (!Running)
                {
                    startTime = DateTime.UtcNow;
                    runTime = null;
                    timer.Start();
                    Running = true;
                }
                else
                {
                    //Reset the timer
                    timer.Stop();
                    timer.Start();
                }
            }
        }

        public TimeSpan GetTimeSpan()
        {
            return runTime ?? DateTime.UtcNow.Subtract(startTime);
        }
    }
}