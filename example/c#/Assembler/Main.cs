using CheatEngine;
using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Assembler
{
    public partial class fmSample : Form
    {
        private CheatEngineLibrary lib;

        public fmSample()
        {
            InitializeComponent();
            lib = new CheatEngineLibrary();
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            lib.loadEngine();
        }

        private void btnUnload_Click(object sender, EventArgs e)
        {
            lib.unloadEngine();
        }

        private void btnProcesses_Click(object sender, EventArgs e)
        {
            string processes;
            lib.iGetProcessList(out processes);
            foreach (string process in Regex.Split(processes, "\r\n"))
                ltBox.Items.Add(process);
        }

        private void btnOpenProcess_Click(object sender, EventArgs e)
        {
            var pid = ltBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(pid))
            {
                return;
            }
            pid = pid.Substring(0, pid.IndexOf('-', 0));
            if (!pid.Equals(""))
            {
                lib.iOpenProcess(pid);
                MessageBox.Show("Process opened");
            }

        }

        private void btnInject_Click(object sender, EventArgs e)
        {
            lib.iAddScript("example", tbScript.Text);
            lib.iActivateRecord(0, true);
        }
    }
}
