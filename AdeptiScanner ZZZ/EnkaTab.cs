using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AdeptiScanner_ZZZ
{
    public partial class EnkaTab : UserControl
    {
        Timer enkaTimer;

        public EnkaTab()
        {
            enkaTimer = new Timer();
            enkaTimer.Interval = 500; //ms
            enkaTimer.Tick += EnkaTimerOnTick;
            InitializeComponent();
            UpdateCooldown();
            enkaTimer.Enabled = true;
        }

        private void EnkaTimerOnTick(object sender, EventArgs e)
        {
            UpdateCooldown();
        }

        private void UpdateCooldown()
        {
            TimeSpan remainingTime = EnkaApi.GetRemainingCooldown();
            // round up, saying cooldown is over too early would be unhelpful
            var displayTime = (int)Math.Ceiling(remainingTime.TotalSeconds);
            string message = "Cooldown: " + displayTime.ToString("D2") + "s";
            label_cooldown.Text = message;
            // colour indication for convenience
            label_cooldown.BackColor = displayTime switch
            {
                > 10 => Color.IndianRed,
                > 0 => Color.Orange,
                _ => Color.Transparent
            };
        }

        private void btn_Fetch_Click(object sender, EventArgs e)
        {
            string uid = new string(text_UID.Text);
            EnkaApi.RequestUid(uid);
        }

        public void UpdateMissingChars(List<Disc> discs, List<Character> characters) 
        {
            
        }
    }
}
