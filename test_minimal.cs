using System;
using System.Windows.Forms;

class MinimalTest : Form
{
    static void Main()
    {
        var f = new MinimalTest();
        f.FormBorderStyle = FormBorderStyle.None;
        f.TopMost = true;
        f.StartPosition = FormStartPosition.Manual;
        f.BackColor = System.Drawing.Color.Red;
        f.Size = new System.Drawing.Size(300, 40);
        f.Location = new System.Drawing.Point(10, 800);

        var lbl = new Label();
        lbl.Text = "HELLO VISIBLE TEST";
        lbl.ForeColor = System.Drawing.Color.White;
        lbl.BackColor = System.Drawing.Color.Red;
        lbl.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        f.Controls.Add(lbl);

        Application.EnableVisualStyles();
        Application.Run(f);
    }
}
