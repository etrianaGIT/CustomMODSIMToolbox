using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RTI.CWR.MODSIM.WQModelingModule
{
    public class WQDebug : System.Windows.Forms.Form
    {
        private DataSet m_DS;
        #region  Windows Form Designer generated code 

        public WQDebug() : base() // ByVal dataView As DataView)
        {

            // This call is required by the Windows Form Designer.
            InitializeComponent();

            // Add any initialization after the InitializeComponent() call
            m_DS = new DataSet();
        }

        // Form overrides dispose to clean up the component list.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // Required by the Windows Form Designer
        private System.ComponentModel.IContainer components;

        // NOTE: The following procedure is required by the Windows Form Designer
        // It can be modified using the Windows Form Designer.  
        // Do not modify it using the code editor.
        private System.Windows.Forms.DataGrid _DataGrid1;

        internal virtual System.Windows.Forms.DataGrid DataGrid1
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get
            {
                return _DataGrid1;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                _DataGrid1 = value;
            }
        }
        [DebuggerStepThrough()]
        private void InitializeComponent()
        {
            _DataGrid1 = new System.Windows.Forms.DataGrid();
            ((System.ComponentModel.ISupportInitialize)_DataGrid1).BeginInit();
            SuspendLayout();
            // 
            // DataGrid1
            // 
            _DataGrid1.DataMember = "";
            _DataGrid1.Dock = System.Windows.Forms.DockStyle.Fill;
            _DataGrid1.Font = new System.Drawing.Font("Microsoft Sans Serif", 6.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            _DataGrid1.HeaderForeColor = System.Drawing.SystemColors.ControlText;
            _DataGrid1.Location = new System.Drawing.Point(0, 0);
            _DataGrid1.Name = "_DataGrid1";
            _DataGrid1.Size = new System.Drawing.Size(484, 273);
            _DataGrid1.TabIndex = 0;
            // 
            // WQDebug
            // 
            AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            ClientSize = new System.Drawing.Size(484, 273);
            Controls.Add(_DataGrid1);
            Name = "WQDebug";
            StartPosition = System.Windows.Forms.FormStartPosition.WindowsDefaultBounds;
            Text = "WQDebug";
            ((System.ComponentModel.ISupportInitialize)_DataGrid1).EndInit();
            ResumeLayout(false);

        }

        #endregion
        public void addTable(DataTable m_localTbl)
        {
            m_DS.Tables.Add(m_localTbl.Copy());
            DataGrid1.DataSource = m_DS;
        }
    }
}