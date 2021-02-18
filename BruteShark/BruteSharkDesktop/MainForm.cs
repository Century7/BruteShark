﻿using BruteSharkDesktop;
using PcapProcessor;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BruteSharkDesktop
{
    public partial class MainForm : Form
    {
        private CancellationTokenSource _cts;
        private HashSet<string> _files;
        private PcapProcessor.Processor _processor;
        private PcapProcessor.Sniffer _sniffer;
        private PcapAnalyzer.Analyzer _analyzer;

        private GenericTableUserControl _passwordsUserControl;
        private HashesUserControl _hashesUserControl;
        private NetworkMapUserControl _networkMapUserControl;
        private SessionsExplorerUserControl _sessionsExplorerUserControl;
        private FilesUserControl _filesUserControl;
        private DnsResponseUserControl _dnsResponseUserControl;


        public MainForm()
        {
            InitializeComponent();

            _files = new HashSet<string>();
            _cts = new CancellationTokenSource();

            // Create the DAL and BLL objects.
            _processor = new PcapProcessor.Processor();
            _sniffer = new PcapProcessor.Sniffer();
            _analyzer = new PcapAnalyzer.Analyzer();
            _processor.BuildTcpSessions = true;
            _processor.BuildUdpSessions = true;

            // Create the user controls. 
            _networkMapUserControl = new NetworkMapUserControl();
            _networkMapUserControl.Dock = DockStyle.Fill;
            _sessionsExplorerUserControl = new SessionsExplorerUserControl();
            _sessionsExplorerUserControl.Dock = DockStyle.Fill;
            _hashesUserControl = new HashesUserControl();
            _hashesUserControl.Dock = DockStyle.Fill;
            _passwordsUserControl = new GenericTableUserControl();
            _passwordsUserControl.Dock = DockStyle.Fill;
            _filesUserControl = new FilesUserControl();
            _filesUserControl.Dock = DockStyle.Fill;
            _dnsResponseUserControl = new DnsResponseUserControl();
            _dnsResponseUserControl.Dock = DockStyle.Fill;

            // Contract the events.
            _sniffer.UdpPacketArived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorUdpPacketToAnalyzerUdpPacket(e.Packet));
            _sniffer.TcpPacketArived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorTcpPacketToAnalyzerTcpPacket(e.Packet));
            _sniffer.TcpSessionArrived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorTcpSessionToAnalyzerTcpSession(e.TcpSession));
            _sniffer.TcpSessionArrived += (s, e) => SwitchToMainThreadContext(() => OnSessionArived(Casting.CastProcessorTcpSessionToBruteSharkDesktopTcpSession(e.TcpSession)));
            _sniffer.UdpSessionArrived += (s, e) => SwitchToMainThreadContext(() => OnSessionArived(Casting.CastProcessorUdpSessionToBruteSharkDesktopUdpSession(e.UdpSession)));
            _processor.UdpPacketArived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorUdpPacketToAnalyzerUdpPacket(e.Packet));
            _processor.TcpPacketArived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorTcpPacketToAnalyzerTcpPacket(e.Packet));
            _processor.TcpSessionArrived += (s, e) => _analyzer.Analyze(CommonUi.Casting.CastProcessorTcpSessionToAnalyzerTcpSession(e.TcpSession));
            _processor.TcpSessionArrived += (s, e) => SwitchToMainThreadContext(() => OnSessionArived(Casting.CastProcessorTcpSessionToBruteSharkDesktopTcpSession(e.TcpSession)));
            _processor.UdpSessionArrived += (s, e) => SwitchToMainThreadContext(() => OnSessionArived(Casting.CastProcessorUdpSessionToBruteSharkDesktopUdpSession(e.UdpSession)));
            _processor.FileProcessingStatusChanged += (s, e) => SwitchToMainThreadContext(() => OnFileProcessingStatusChanged(s, e));
            _processor.ProcessingPrecentsChanged += (s, e) => SwitchToMainThreadContext(() => OnProcessingPrecentsChanged(s, e));
            _processor.ProcessingFinished += (s, e) => SwitchToMainThreadContext(() => OnProcessingFinished(s, e));
            _analyzer.ParsedItemDetected += (s, e) => SwitchToMainThreadContext(() => OnParsedItemDetected(s, e));

            InitilizeFilesIconsList();
            InitilizeModulesCheckedListBox();
            InitilizeInterfacesComboBox();
            this.modulesTreeView.ExpandAll();
        }

        private void InitilizeInterfacesComboBox()
        {
            foreach (string interfaceName in _sniffer.AvailiableDevicesNames)
            {
                this.interfacesComboBox.Items.Add(interfaceName);
            }
        }

        private void InitilizeModulesCheckedListBox()
        {
            foreach (var module_name in _analyzer.AvailableModulesNames)
            {
                this.modulesCheckedListBox.Items.Add(module_name, isChecked: false);
            }
        }

        private void OnProcessingFinished(object sender, EventArgs e)
        {
            this.progressBar.Value = this.progressBar.Maximum;
            HandleFailedFiles();
        }

        private void HandleFailedFiles()
        {
            // The tag holds the full file path.
            var failedFilesString = string.Join(
                Environment.NewLine,
                filesListView.Items
                    .Cast<ListViewItem>()
                    .Where(x => x.SubItems[2].Text == "Failed")
                    .Select(x => x.Tag.ToString() + Environment.NewLine)
                    .ToList());

            if (failedFilesString.Length > 0)
            {
                var failedFilesMessage =
@$"BruteShark failed to analyze to following files:
{Environment.NewLine}{failedFilesString}
 Note: if your files are in PCAPNG format it possible to convert them to a PCAP format using Tshark: 
tshark -F pcap -r <pcapng file> -w <pcap file>";

                MessageBox.Show(failedFilesMessage);
            }
        }

        private void OnSessionArived(TransportLayerSession session)
        {
            _sessionsExplorerUserControl.AddSession(session);
            this.modulesTreeView.Nodes["NetworkNode"].Nodes["SessionsNode"].Text = $"Sessions ({_sessionsExplorerUserControl.SessionsCount})";
        }

        private void SwitchToMainThreadContext(Action func)
        {
            // Thread-Safe mechanizm:
            // Check if we are currently runing in a different thread than the one that 
            // control was created on, if so we invoke a call to our function again, but because 
            // we used the invoke method again from our form the caller this time will be the 
            // the thread that created the form.
            // For more detailes: 
            // https://docs.microsoft.com/en-us/dotnet/framework/winforms/controls/how-to-make-thread-safe-calls-to-windows-forms-controls
            if (InvokeRequired)
            {
                Invoke(func);
                return;
            }

            Invoke(func);
        }

        private void OnProcessingPrecentsChanged(object sender, PcapProcessor.ProcessingPrecentsChangedEventArgs e)
        {
            if (e.Precents <= 90)
            {
                this.progressBar.Value = e.Precents;
            }
        }

        private void OnFileProcessingStatusChanged(object sender, FileProcessingStatusChangedEventArgs e)
        {
            var currentFileListViewItem = this.filesListView.FindItemWithText(
                Path.GetFileName(e.FilePath),
                true,
                0,
                false);

            if (e.Status == FileProcessingStatus.Started)
            {
                currentFileListViewItem.ForeColor = Color.Red;
                currentFileListViewItem.SubItems[2].Text = "On Process..";
            }
            else if (e.Status == FileProcessingStatus.Finished)
            {
                currentFileListViewItem.ForeColor = Color.Blue;
                currentFileListViewItem.SubItems[2].Text = "Analyzed";
            }
            else if (e.Status == FileProcessingStatus.Faild)
            {
                currentFileListViewItem.ForeColor = Color.DarkOrange;
                currentFileListViewItem.SubItems[2].Text = "Failed";
            }
        }

        private void InitilizeFilesIconsList()
        {
            this.filesListView.SmallImageList = new ImageList();
            ImageList imgList = new ImageList();
            imgList.ImageSize = new Size(22, 22);
            imgList.Images.Add(Properties.Resources.Wireshark_Icon);
            this.filesListView.SmallImageList = imgList;
        }

        private void OnParsedItemDetected(object sender, PcapAnalyzer.ParsedItemDetectedEventArgs e)
        {
            if (e.ParsedItem is PcapAnalyzer.NetworkPassword)
            {
                var password = e.ParsedItem as PcapAnalyzer.NetworkPassword;
                _passwordsUserControl.AddDataToTable(password);
                this.modulesTreeView.Nodes["CredentialsNode"].Nodes["PasswordsNode"].Text = $"Passwords ({_passwordsUserControl.ItemsCount})";
                _networkMapUserControl.HandlePassword(password);
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkHash)
            {
                var hash = e.ParsedItem as PcapAnalyzer.NetworkHash;
                _hashesUserControl.AddHash(hash);
                this.modulesTreeView.Nodes["CredentialsNode"].Nodes["HashesNode"].Text = $"Hashes ({_hashesUserControl.HashesCount})";
                _networkMapUserControl.HandleHash(hash);
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkConnection)
            {
                var connection = e.ParsedItem as PcapAnalyzer.NetworkConnection;
                _networkMapUserControl.AddEdge(connection.Source, connection.Destination);
                this.modulesTreeView.Nodes["NetworkNode"].Nodes["NetworkMapNode"].Text = $"Network Map ({_networkMapUserControl.NodesCount})";
            }
            else if (e.ParsedItem is PcapAnalyzer.NetworkFile)
            {
                var fileObject = e.ParsedItem as PcapAnalyzer.NetworkFile;
                _filesUserControl.AddFile(fileObject);
                this.modulesTreeView.Nodes["DataNode"].Nodes["FilesNode"].Text = $"Files ({_filesUserControl.FilesCount})";
            }
            else if (e.ParsedItem is PcapAnalyzer.DnsNameMapping)
            {
                var dnsResponse = e.ParsedItem as PcapAnalyzer.DnsNameMapping;
                _dnsResponseUserControl.AddNameMapping(dnsResponse);
                this.modulesTreeView.Nodes["NetworkNode"].Nodes["DnsResponsesNode"].Text = $"DNS Responses ({_dnsResponseUserControl.AnswerCount})";
                _networkMapUserControl.HandleDnsNameMapping(dnsResponse);
            }
        }

        private void addFilesButton_Click(object sender, EventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string filePath in openFileDialog.FileNames)
                {
                    AddFile(filePath);
                }
            }
        }

        private void AddFile(string filePath)
        {
            _files.Add(filePath);

            var listViewRow = new ListViewItem(
                new string[]
                {
                    Path.GetFileName(filePath),
                    new FileInfo(filePath).Length.ToString(),
                    "Wait"
                }
                , 0);

            // TODO: think of binding
            listViewRow.Tag = filePath;
            this.filesListView.Items.Add(listViewRow);
        }

        private void RunButton_Click(object sender, EventArgs e)
        {
            new Thread(() => _processor.ProcessPcaps(this._files)).Start();
        }

        private void ModulesTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.modulesSplitContainer.Panel2.Controls.Clear();

            switch (e.Node.Name)
            {
                case "PasswordsNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_passwordsUserControl);
                    break;
                case "HashesNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_hashesUserControl);
                    break;
                case "NetworkMapNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_networkMapUserControl);
                    break;
                case "SessionsNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_sessionsExplorerUserControl);
                    break;
                case "FilesNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_filesUserControl);
                    break;
                case "DnsResponsesNode":
                    this.modulesSplitContainer.Panel2.Controls.Add(_dnsResponseUserControl);
                    break;
                default:
                    break;
            }
        }

        private void RemoveFilesButton_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in filesListView.SelectedItems)
            {
                _files.Remove(item.Tag.ToString());
                item.Remove();
            }
        }

        private void ModulesCheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            var module_name = ((CheckedListBox)sender).Text;

            if (e.NewValue == CheckState.Checked)
            {
                _analyzer.AddModule(module_name);
            }
            else
            {
                _analyzer.RemoveModule(module_name);
            }
        }

        private void BuildTcpSessionsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (buildTcpSessionsCheckBox.CheckState == CheckState.Checked)
            {
                buildTcpSessionsCheckBox.Text = "Build TCP Sessions: ON";
                this._processor.BuildTcpSessions = true;
            }
            else if (buildTcpSessionsCheckBox.CheckState == CheckState.Unchecked)
            {
                buildTcpSessionsCheckBox.Text = "Build TCP Sessions: OFF";
                this._processor.BuildTcpSessions = false;
                MessageOnBuildSessionsConfigurationChanged();
            }
        }

        private void BuildUdpSessionsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (buildUdpSessionsCheckBox.CheckState == CheckState.Checked)
            {
                buildUdpSessionsCheckBox.Text = "Build UDP Sessions: ON";
                this._processor.BuildUdpSessions = true;
            }
            else if (buildUdpSessionsCheckBox.CheckState == CheckState.Unchecked)
            {
                buildUdpSessionsCheckBox.Text = "Build UDP Sessions: OFF";
                this._processor.BuildUdpSessions = false;
                MessageOnBuildSessionsConfigurationChanged();
            }
        }

        private void MessageOnBuildSessionsConfigurationChanged()
        {
            MessageBox.Show(@"NOTE, Disabling sessions reconstruction means that BruteShark will not analyze full sessions,
This means a faster processing but also that some obects may not be extracted.");
        }

        private void LiveCaptureButton_Click(object sender, EventArgs e)
        {
            if (this.interfacesComboBox.SelectedItem == null)
            {
                MessageBox.Show("No interface selected");
                return;
            }

            _sniffer.SelectedDeviceName = this.interfacesComboBox.SelectedItem.ToString();
            StartLiveCaptureAsync();
        }

        private async void StartLiveCaptureAsync()
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            await Task.Run(() => _sniffer.StartSniffing(ct));

            // We wait here until the sniffing will be stoped (by the stop button).
            MessageBox.Show("Capture Stoped");
        }

        private void StopCaptureButton_Click(object sender, EventArgs e)
        {
            _cts.Cancel();
        }
    }
}
    