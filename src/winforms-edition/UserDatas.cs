using FilterVisualPlatform.FilterVisualPlatform;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilterVisualPlatform
{
    public partial class UserDatas : UserControl
    {
        private UserComm _userComm;

        public AdaptiveKalmanFilter adaptiveKalmanFilter1 = new AdaptiveKalmanFilter();
        public AdaptiveKalmanFilter adaptiveKalmanFilter2 = new AdaptiveKalmanFilter();
        public AdaptiveKalmanFilter adaptiveKalmanFilter3 = new AdaptiveKalmanFilter();
        public AdaptiveKalmanFilter adaptiveKalmanFilter4 = new AdaptiveKalmanFilter();


        public OneEuroLikeFilter oneEuroLikeFilter1 = new OneEuroLikeFilter(0.1f);
        public OneEuroLikeFilter oneEuroLikeFilter2 = new OneEuroLikeFilter(0.1f);
        public OneEuroLikeFilter oneEuroLikeFilter3 = new OneEuroLikeFilter(0.1f);
        public OneEuroLikeFilter oneEuroLikeFilter4 = new OneEuroLikeFilter(0.1f);

        public RobustHInfinityFilter robustHInfinityFilter1 = new RobustHInfinityFilter();
        public RobustHInfinityFilter robustHInfinityFilter2 = new RobustHInfinityFilter();
        public RobustHInfinityFilter robustHInfinityFilter3 = new RobustHInfinityFilter();
        public RobustHInfinityFilter robustHInfinityFilter4 = new RobustHInfinityFilter();


        public UserDatas(UserComm userComm)
        {
            InitializeComponent();

            _userComm = userComm;
        }

        private void UserDatas_Load(object sender, EventArgs e)
        {
            timer1.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_userComm == null) return;

            lb_Raw.Text = _userComm.Raw.ToString("F2");
            lb_filtered.Text = _userComm.Filtered.ToString("F2");
            lb_TofRaw.Text = _userComm.TofRaw.ToString("F2");
            lb_tofFilt.Text = _userComm.TofFiltered.ToString("F2");


            lb_KFRaw.Text = adaptiveKalmanFilter1.Update(_userComm.Raw).ToString("F2");
            lb_KFfiltered.Text = adaptiveKalmanFilter2.Update(_userComm.Filtered).ToString("F2");
            lb_KFtofRaw.Text = adaptiveKalmanFilter3.Update(_userComm.TofRaw).ToString("F2");
            lb_KFtofFilt.Text = adaptiveKalmanFilter4.Update(_userComm.TofFiltered).ToString("F2");

            lb_OERaw.Text = oneEuroLikeFilter1.Update(_userComm.Raw).ToString("F2");
            lb_OEfiltered.Text = oneEuroLikeFilter2.Update(_userComm.Filtered).ToString("F2");
            lb_OEtofRaw.Text = oneEuroLikeFilter3.Update(_userComm.TofRaw).ToString("F2");
            lb_OEtofFilt.Text = oneEuroLikeFilter4.Update(_userComm.TofFiltered).ToString("F2");

            lb_RHRaw.Text = robustHInfinityFilter1.Update(_userComm.Raw).ToString("F2");
            lb_RHfiltered.Text = robustHInfinityFilter2.Update(_userComm.Filtered).ToString("F2");
            lb_RHtofRaw.Text = robustHInfinityFilter3.Update(_userComm.TofRaw).ToString("F2");
            lb_RHtofFilt.Text = robustHInfinityFilter4.Update(_userComm.TofFiltered).ToString("F2");


        }
    }
}
