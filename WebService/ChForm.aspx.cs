using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.Web.UI.DataVisualization.Charting;



namespace mzAccess {
    public partial class ChForm : System.Web.UI.Page {

        static mzAccessService.MSDataService Service = new mzAccessService.MSDataService();

        public static Color[] ColorsDefault = 
        {
            Color.FromArgb(65,140,240),
            Color.FromArgb(252,180,65),
            Color.FromArgb(224,64,10),
            Color.FromArgb(5,100,146),
            Color.FromArgb(191,191,191),
            Color.FromArgb(26,59,105),
            Color.FromArgb(255,227,130),
            Color.FromArgb(18,156,221),
            Color.FromArgb(202,107,75),
            Color.FromArgb(0,92,219),
            Color.FromArgb(243,210,136),
            Color.FromArgb(80,99,129),
            Color.FromArgb(241,185,168),
            Color.FromArgb(224,131,10),
            Color.FromArgb(120,147,190),
        };

        protected void Page_Load(object sender, EventArgs e) {
            //Parameters parsing
            string FileStr = null;
            if(Request != null && !IsPostBack) {
                FileStr = Request["files"];
                TextBox5.Text = FileStr;
                double MinRT = Convert.ToDouble(Request["minrt"]);
                double MaxRT = Convert.ToDouble(Request["maxrt"]);
                double MinMZ = Convert.ToDouble(Request["minmz"]);
                double MaxMZ = Convert.ToDouble(Request["maxmz"]);
                TextBox1.Text = String.Format("{0:0.0##}",MinRT);
                TextBox2.Text = String.Format("{0:0.0##}",MaxRT);
                TextBox3.Text = String.Format("{0:0.0####}",MinMZ);
                TextBox4.Text = String.Format("{0:0.0####}",MaxMZ);
            }else {
                FileStr = TextBox5.Text;
            }
            if(String.IsNullOrWhiteSpace(FileStr)) {
                return;
            }
            string[] Files = null;
            string EM;
            if (FileStr.Contains("*") || FileStr.Contains("*")) {
                Files = Service.FileList(FileStr, out EM);
            }else {
                Files = FileStr.Split(new char[] { '|' });
            }
            bool[] Selected = new bool[Files.Length];
            for(int i = 0 ; i < Files.Length ; i++) {
                Selected[i] = true;
                for ( int j = 0 ; j < CheckBoxList1.Items.Count ; j++) {
                    if (CheckBoxList1.Items[j].Text == Files[i]) {
                        Selected[i] = CheckBoxList1.Items[j].Selected;
                    }
                }
            }
            CheckBoxList1.Items.Clear();
            for(int i = 0 ; i < Files.Length ; i++) {
                ListItem LI = new ListItem(Files[i]);
                LI.Selected = Selected[i];
                CheckBoxList1.Items.Add(LI);
            }
            //if no parameterrs - use default (inserted at design time or in session)
            ShowChart();
        }

        protected void ShowChart() {
            //if no parameterrs - use default (inserted at design time or in session)
            double MinRT = Convert.ToDouble(TextBox1.Text);
            double MaxRT = Convert.ToDouble(TextBox2.Text);
            double MinMZ = Convert.ToDouble(TextBox3.Text);
            double MaxMZ = Convert.ToDouble(TextBox4.Text);
            Chart1.Series.Clear();
            for(int i = 0 ; i < CheckBoxList1.Items.Count ; i++) {
                string File = CheckBoxList1.Items[i].Text;
                string Error;
                if(!CheckBoxList1.Items[i].Selected)
                    continue;
                double[] ChartPoints = Service.GetChromatogram(File,MinMZ,MaxMZ,MinRT,MaxRT,true,out Error);
                Series Seria = new Series();
                Seria.ChartType = SeriesChartType.FastLine;
                Seria.Color = ColorsDefault[i % 15];
                for(int j = 0 ; j < ChartPoints.Length ; j++) {
                    System.Web.UI.DataVisualization.Charting.DataPoint DP = new System.Web.UI.DataVisualization.Charting.DataPoint();
                    DP.XValue = ChartPoints[j];
                    j++;
                    DP.YValues = new double[] { ChartPoints[j] };
                    Seria.Points.Add(DP);
                }
                Chart1.Series.Add(Seria);
            }
            Chart1.ChartAreas[0].AxisX.Minimum = MinRT;
            Chart1.ChartAreas[0].AxisX.Maximum = MaxRT;
        }

        protected void CheckBoxList1_SelectedIndexChanged(object sender, EventArgs e) {
            ShowChart();
        }

    }
}