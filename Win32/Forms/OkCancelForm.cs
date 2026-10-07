using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Bemo.Win32.Forms
{
    public partial class OkCancelForm : Form
    {
        public OkCancelForm()
        {
            InitializeComponent();
        }
        public OkCancelForm(Control child)
        {
            InitializeComponent();
            child.Dock = DockStyle.Fill;
            this.mainPanel.Controls.Add(child);
        }
    }
}
