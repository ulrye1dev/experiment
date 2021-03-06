﻿using System.Diagnostics.Contracts;
using System.Windows.Forms;

namespace ReportUtils
{
    public abstract class BaseReport
    {
        public const string DocConfigFile = ".openpublishing.publish.config.json";

        public const string ArticlesRoot1 = "articles";
        public const string ArticlesRoot2 = "composer-docs";

        public RichTextBox Status { get; private set; }

        public SaveFileDialog SaveDialog { get; private set; }

        public BaseReport(RichTextBox status, SaveFileDialog saveDialog)
        {
            Status = status;
            SaveDialog = saveDialog;
        }

        public virtual bool Run()
        {
            Contract.Requires(Status != null);
            Contract.Requires(SaveDialog != null);

            return false;
        }
    }
}
