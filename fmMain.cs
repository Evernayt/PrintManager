using System;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Printing;
using System.IO;
using System.Diagnostics;
using System.Printing;
using System.Management;
using System.ServiceProcess;
using System.ComponentModel;
using Print_Manager.Properties;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Principal;
using System.Linq;

namespace Print_Manager
{
    public partial class fmMain : Form
    {
        private string selectedPrinterFullName;
        private string selectedPrinterShortName;
        private string[] virtualPrinters;
        private string[] laserPrinters;
        private string[] plotters;
        private bool isLoading = false;
        private const string PAUSED = " — Приостановлен";
        private PrintServer printServer;
        private ToolTip toolTip;

        private bool IsRunAsAdministrator()
        {
            WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();
            WindowsPrincipal windowsPrincipal = new WindowsPrincipal(windowsIdentity);

            return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public fmMain()
        {
            InitializeComponent();

            dataGridView1.Columns["isDefaultPrinter"].DefaultCellStyle.NullValue = null;
            dataGridView1.Columns["isNetworkPrinter"].DefaultCellStyle.NullValue = null;
            dataGridView1.Columns["QueueState"].DefaultCellStyle.NullValue = null;

            dataGridView1.ShowCellToolTips = false;

            Text += $" v.{Application.ProductVersion}";
        }

        private void fmMain_Load(object sender, EventArgs e)
        {
            tsmiHideLaserPrinters.Checked = Settings.Default.HideLaserPrinters;
            tsmiHidePlotters.Checked = Settings.Default.HidePlotters;
            tsmiHideVirtualPrinters.Checked = Settings.Default.HideVirtualPrinters;

            ReadPrinters();
            LoadPrintersToDVG();
        }

        private void PrintUI(string arg, string printerName)
        {
            Process.Start("RunDll32.exe", $"Printui.dll,PrintUIEntry /{ arg } /n \"{ printerName }\"");
        }

        private string GetPrinterShortName(string printerFullName)
        {
            return Path.GetFileName(printerFullName);
        }

        private void DeleteLocalPrinter(string printerName)
        {
            try
            {
                PrintUI("dl", printerName);
                string printerShortName = GetPrinterShortName(printerName);
                Notification($"Принтер {printerName} удален", Color.Green);
            }
            catch
            {
                Notification("Не удалось удалить принтер", Color.Red);
            }
        }

        private void SpotTroubleUsingQueueAttributes(ref string statusReport, PrintQueue pq)
        {
            if ((pq.QueueStatus & PrintQueueStatus.None) == PrintQueueStatus.None)
                statusReport += "Готов";
            else if ((pq.QueueStatus & PrintQueueStatus.Error) == PrintQueueStatus.Error)
                statusReport += "В состоянии ошибки";
            else if ((pq.QueueStatus & PrintQueueStatus.ServerUnknown) == PrintQueueStatus.ServerUnknown)
                statusReport += "В состоянии ошибки";
            else if ((pq.QueueStatus & PrintQueueStatus.PaperProblem) == PrintQueueStatus.PaperProblem)
                statusReport += "Проблемы с бумагой";
            else if ((pq.QueueStatus & PrintQueueStatus.NoToner) == PrintQueueStatus.NoToner)
                statusReport += "Закончился тонер (краска)";
            else if ((pq.QueueStatus & PrintQueueStatus.DoorOpen) == PrintQueueStatus.DoorOpen)
                statusReport += "Открыта крышка";
            else if ((pq.QueueStatus & PrintQueueStatus.NotAvailable) == PrintQueueStatus.NotAvailable)
                statusReport += "Недоступен";
            else if ((pq.QueueStatus & PrintQueueStatus.Offline) == PrintQueueStatus.Offline)
                statusReport += "Отключен";
            else if ((pq.QueueStatus & PrintQueueStatus.OutOfMemory) == PrintQueueStatus.OutOfMemory)
                statusReport += "Недостаточно памяти";
            else if ((pq.QueueStatus & PrintQueueStatus.PaperOut) == PrintQueueStatus.PaperOut)
                statusReport += "Закончилась бумага";
            else if ((pq.QueueStatus & PrintQueueStatus.OutputBinFull) == PrintQueueStatus.OutputBinFull)
                statusReport += "Выходной лоток заполнен";
            else if ((pq.QueueStatus & PrintQueueStatus.PaperJam) == PrintQueueStatus.PaperJam)
                statusReport += "Замятие бумаги";
            else if ((pq.QueueStatus & PrintQueueStatus.Paused) == PrintQueueStatus.Paused)
                statusReport += "Приостановлен";
            else if ((pq.QueueStatus & PrintQueueStatus.TonerLow) == PrintQueueStatus.TonerLow)
                statusReport += "Мало тонера (краски)";
            else if ((pq.QueueStatus & PrintQueueStatus.UserIntervention) == PrintQueueStatus.UserIntervention)
                statusReport += "Требуется вмешательство пользователя";
            else if ((pq.QueueStatus & PrintQueueStatus.Busy) == PrintQueueStatus.Busy)
                statusReport += "Занят";
            else if ((pq.QueueStatus & PrintQueueStatus.Printing) == PrintQueueStatus.Printing)
                statusReport += "Печатает";
            else if ((pq.QueueStatus & PrintQueueStatus.Waiting) == PrintQueueStatus.Waiting)
                statusReport += "Ожидает задания на печать";
            else if ((pq.QueueStatus & PrintQueueStatus.PowerSave) == PrintQueueStatus.PowerSave)
                statusReport += "Находится в режиме энергосбережения";
            else if ((pq.QueueStatus & PrintQueueStatus.IOActive) == PrintQueueStatus.IOActive)
                statusReport += "Обменивается данными с сервером печати";
            else if ((pq.QueueStatus & PrintQueueStatus.Initializing) == PrintQueueStatus.Initializing)
                statusReport += "Инициализация";
            else if ((pq.QueueStatus & PrintQueueStatus.PagePunt) == PrintQueueStatus.PagePunt)
                statusReport += "Не удается напечатать текущую страницу";
            else if ((pq.QueueStatus & PrintQueueStatus.PendingDeletion) == PrintQueueStatus.PendingDeletion)
                statusReport += "Идет удаление задания из очереди печати";
        }

        private void CellColor()
        {
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (row.Cells["Status"].Value.ToString() == "Отключен")
                    row.DefaultCellStyle.ForeColor = Color.Gray;
                else if (row.Cells["Status"].Value.ToString() == "В состоянии ошибки")
                    row.DefaultCellStyle.ForeColor = Color.Maroon;
                else if (Convert.ToBoolean(row.Cells["isPausedPrinter"].Value))
                    row.DefaultCellStyle.ForeColor = Color.Maroon;
                else
                    row.DefaultCellStyle.ForeColor = Color.Empty;
            }
        }

        private void Loader(bool isShowing, bool isError = false)
        {
            isLoading = isShowing;

            if (isShowing)
            {
                apbGif.StartAnimation();
                if (isError)
                {
                    isLoading = false;
                    apbGif.Image = Resources.error;
                    lblLoaderMsg.Text = "Ошибка сервера печати";
                    btnLoader.Visible = true;
                }
                else
                {
                    apbGif.Image = Resources.loading;
                    lblLoaderMsg.Text = "Обновление";
                    btnLoader.Visible = false;
                }
            }
            else
            {
                apbGif.StopAnimation();
            }

            pInfo.Visible = isShowing;
            dataGridView1.Visible = !isShowing;
        }

        private async void LoadPrintersToDVG(bool notif = true)
        {
            try
            {
                if (isLoading) return;
                Loader(true);
                dataGridView1.Rows.Clear();

                await Task.Run(() =>
                {
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, WorkOffline FROM Win32_Printer");
                    ManagementObjectCollection searchers = searcher.Get();
                    PrintServer newPrintServer = new PrintServer();
                    PrintQueueCollection printQueues = newPrintServer.GetPrintQueues();
                    Invoke((MethodInvoker)(() => printServer = newPrintServer));

                    foreach (ManagementObject win32_printer in searchers.Cast<ManagementObject>())
                    {
                        string printerFullName = win32_printer["Name"].ToString();
                        bool isOffline = (bool)win32_printer["WorkOffline"];

                        PrinterSettings printer = new PrinterSettings
                        {
                            PrinterName = printerFullName
                        };

                        if (printer.IsValid)
                        {
                            string printerJobsCount = "";
                            string printerStatus = "";
                            string printerName = GetPrinterShortName(printerFullName);
                            bool isDefaultPrinter = printer.IsDefaultPrinter;
                            bool isNetworkPrinter = printerName != printerFullName;
                            bool isPausedPrinter = false;

                            foreach (PrintQueue pq in printQueues)
                            {
                                if (printerFullName == pq.Name)
                                {
                                    isPausedPrinter = pq.IsPaused;

                                    printerJobsCount = pq.NumberOfJobs.ToString();
                                    if (isOffline)
                                    {
                                        printerStatus = "Отключен";
                                    }
                                    else
                                    {
                                        SpotTroubleUsingQueueAttributes(ref printerStatus, pq);
                                    }
                                    break;
                                }
                            }

                            if (!IsNeedFiltered(printerName))
                            {
                                int rowNum = 0;
                                Invoke((MethodInvoker)(() => rowNum = dataGridView1.Rows.Add()));

                                if (isDefaultPrinter)
                                    dataGridView1.Rows[rowNum].Cells["isDefaultPrinter"].Value = Resources.check;

                                if (isNetworkPrinter)
                                    dataGridView1.Rows[rowNum].Cells["isNetworkPrinter"].Value = Resources.wifi;

                                dataGridView1.Rows[rowNum].Cells["Printer"].Value = printerName;
                                dataGridView1.Rows[rowNum].Cells["PrintQueue"].Value = printerJobsCount;
                                dataGridView1.Rows[rowNum].Cells["Status"].Value = printerStatus;
                                dataGridView1.Rows[rowNum].Cells["PrinterFullName"].Value = printerFullName;
                                dataGridView1.Rows[rowNum].Cells["Default"].Value = isDefaultPrinter;

                                if (isPausedPrinter)
                                {
                                    dataGridView1.Rows[rowNum].Cells["QueueState"].Value = Resources.play;
                                    dataGridView1.Rows[rowNum].Cells["isPausedPrinter"].Value = true;
                                    dataGridView1.Rows[rowNum].Cells["Status"].Value += PAUSED;
                                }
                                else if (!isNetworkPrinter)
                                {
                                    dataGridView1.Rows[rowNum].Cells["QueueState"].Value = Resources.pause;
                                }

                                if (printerFullName == selectedPrinterFullName)
                                {
                                    dataGridView1.Rows[rowNum].Selected = true;
                                }
                            }
                        }
                    }

                    CellColor();
                });

                Loader(false);
                dataGridView1.Sort(dataGridView1.Columns["Printer"], ListSortDirection.Ascending);
                if (notif) Notification("Обновлено", Color.Green);
            }
            catch
            {
                Loader(true, true);
            }
        }

        private bool IsNeedFiltered(string printerName)
        {
            if (tsmiHideLaserPrinters.Checked)
            {
                foreach (string laserPrinter in laserPrinters)
                {
                    if (printerName.ToLower().Contains(laserPrinter))
                        return true;
                }
            }

            if (tsmiHidePlotters.Checked)
            {
                foreach (string plotter in plotters)
                {
                    if (printerName.ToLower().Contains(plotter))
                        return true;
                }
            }

            if (tsmiHideVirtualPrinters.Checked)
            {
                foreach (string virtualPrinter in virtualPrinters)
                {
                    if (printerName.ToLower().Contains(virtualPrinter))
                        return true;
                }
            }

            return false;
        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex != -1 && e.ColumnIndex != 7)
                PrintUI("o", dataGridView1.CurrentRow.Cells["PrinterFullName"].Value.ToString());
        }

        private void openPrintQueueToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
                PrintUI("o", selectedPrinterFullName);
        }

        private void useAsDefaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
            {
                try
                {
                    PrintUI("y", selectedPrinterFullName);
                    Notification($"{selectedPrinterShortName} установлен по умолчанию", Color.Green);
                    LoadPrintersToDVG(false);
                }
                catch
                {
                    Notification($"Не удалось установить {selectedPrinterShortName} по умолчанию", Color.Red);
                }
            }
        }

        private void printATestPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
                PrintUI("k", selectedPrinterFullName);
        }

        private void Notification(string message, Color color)
        {
            tslblNotifications.Text = $"{message} — {DateTime.Now.ToShortTimeString()}";
            tslblNotifications.ForeColor = color;
            timer1.Stop();
            timer1.Start();
        }

        private void NotificationClear()
        {
            tslblNotifications.Text = "";
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            NotificationClear();
            timer1.Stop();
        }

        private void deletePrinterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
            {
                string message = $"Вы действительно хотите удалить принтер {selectedPrinterShortName}?";
                var result = MessageBox.Show(message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (result == DialogResult.Yes)
                {
                    DeleteLocalPrinter(selectedPrinterFullName);
                }
            }
        }

        private void printerPropertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
                PrintUI("p", selectedPrinterFullName);
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex != -1)
            {
                selectedPrinterFullName = dataGridView1["PrinterFullName", e.RowIndex].Value.ToString();
                selectedPrinterShortName = dataGridView1["Printer", e.RowIndex].Value.ToString();

                if (e.Button == MouseButtons.Right)
                {
                    dataGridView1[e.ColumnIndex, e.RowIndex].Selected = true;
                    bool isDefaultPrinter = Convert.ToBoolean(dataGridView1["Default", e.RowIndex].Value);
                    useAsDefaultToolStripMenuItem.Checked = isDefaultPrinter;
                }
                else if (e.ColumnIndex == 7)
                {
                    DataGridViewRow row = dataGridView1.Rows[e.RowIndex];

                    if (row.Cells[e.ColumnIndex].Value == null) return;

                    try
                    {
                        PrintQueue printQueue = new PrintQueue(printServer, selectedPrinterFullName, PrintSystemDesiredAccess.AdministratePrinter);

                        if (Convert.ToBoolean(row.Cells["isPausedPrinter"].Value))
                        {
                            printQueue.Resume();
                            row.Cells["isPausedPrinter"].Value = false;
                            row.Cells[e.ColumnIndex].Value = Resources.pause_hover;
                            row.Cells["Status"].Value = row.Cells["Status"].Value.ToString().Replace(PAUSED, "");
                        }
                        else
                        {
                            printQueue.Pause();
                            row.Cells["isPausedPrinter"].Value = true;
                            row.Cells[e.ColumnIndex].Value = Resources.play_hover;
                            row.Cells["Status"].Value += PAUSED;
                        }

                        CellColor();
                    }
                    catch (Exception)
                    {
                        Loader(true, true);
                    }
                }
            }
        }

        private void tsmiRefresh_Click(object sender, EventArgs e)
        {
            LoadPrintersToDVG();
        }

        private void RunAsAdministrator()
        {
            if (!IsRunAsAdministrator())
            {
                var processInfo = new ProcessStartInfo(Assembly.GetExecutingAssembly().CodeBase)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    Process.Start(processInfo);
                    Application.Exit();
                }
                catch (Exception)
                {
                    MessageBox.Show("Это приложение должно быть запущено от имени администратора.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private async void ReloadPrintServer()
        {
            if (isLoading) return;

            RunAsAdministrator();

            Loader(true);
            Notification("Идет перезапуск сервера печати", Color.Blue);

            try
            {
                await Task.Run(() =>
                {
                    using (ServiceController service = new ServiceController("Spooler"))
                    {
                        if ((!service.Status.Equals(ServiceControllerStatus.Stopped)) &&
                        (!service.Status.Equals(ServiceControllerStatus.StopPending)))
                        {
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped);
                        }

                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                });

                Loader(false);
                LoadPrintersToDVG(false);

                Notification("Сервер печати перезагружен", Color.Green);
            }
            catch
            {
                Loader(false);
                Notification("Не удалось перезагрузить сервер печати", Color.Red);
            }
        }

        private void tsmiReloadPrintServer_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Вы действительно хотите перезагрузить сервер печати?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (dr == DialogResult.Yes)
            {
                ReloadPrintServer();
            }
        }

        private void tsmiHideLaserPrinters_Click(object sender, EventArgs e)
        {
            bool isChecked = tsmiHideLaserPrinters.Checked;
            tsmiHideLaserPrinters.Checked = !isChecked;
            Settings.Default.HideLaserPrinters = !isChecked;
            Settings.Default.Save();

            LoadPrintersToDVG();
        }

        private void tsmiHidePlotters_Click(object sender, EventArgs e)
        {
            bool isChecked = tsmiHidePlotters.Checked;
            tsmiHidePlotters.Checked = !isChecked;
            Settings.Default.HidePlotters = !isChecked;
            Settings.Default.Save();

            LoadPrintersToDVG();
        }

        private void tsmiHideVirtualPrinters_Click(object sender, EventArgs e)
        {
            bool isChecked = tsmiHideVirtualPrinters.Checked;
            tsmiHideVirtualPrinters.Checked = !isChecked;
            Settings.Default.HideVirtualPrinters = !isChecked;
            Settings.Default.Save();

            LoadPrintersToDVG();
        }

        private async void tsmiClearPrintServerFolder_Click(object sender, EventArgs e)
        {
            DialogResult dr = MessageBox.Show("Вы действительно хотите очистить папку?\nЭто очистит очередь печати у всех принтеров!",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (dr == DialogResult.Yes)
            {
                if (isLoading) return;

                RunAsAdministrator();

                Loader(true);
                Notification("Идет очистка папки сервера печати", Color.Blue);

                try
                {
                    await Task.Run(() =>
                    {
                        using (ServiceController service = new ServiceController("Spooler"))
                        {
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped);
                            string path = Environment.SystemDirectory + @"\spool\PRINTERS";
                            Directory.Delete(path, true);
                            Directory.CreateDirectory(path);

                            service.Start();
                            service.WaitForStatus(ServiceControllerStatus.Running);
                        }
                    });

                    Loader(false);
                    LoadPrintersToDVG(false);

                    Notification("Папка сервера печати очищена", Color.Green);
                }
                catch
                {
                    Loader(false);
                    Notification("Не удалось очистить папку сервера печати", Color.Red);
                }
            }
        }

        private void tsmiDevicesAndPrinters_Click(object sender, EventArgs e)
        {
            string controlpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "control.exe");
            Process.Start(controlpath, "/name Microsoft.DevicesAndPrinters");
        }

        private void printSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView1.CurrentRow.Index != -1)
                PrintUI("e", selectedPrinterFullName);

        }

        private void fmMain_SizeChanged(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Normal || WindowState == FormWindowState.Maximized)
                LoadPrintersToDVG();
        }

        private void installPrinterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PrintUI("il", null);
        }

        private void ReadPrinters()
        {
            virtualPrinters = Settings.Default.VirtualPrinters.Split(',');
            plotters = Settings.Default.Plotters.Split(',');
            laserPrinters = Settings.Default.LaserPrinters.Split(',');
        }

        private void btnLoader_Click(object sender, EventArgs e)
        {
            ReloadPrintServer();
        }

        private void renamePrinterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (fmRename rename = new fmRename(selectedPrinterFullName))
            {
                rename.ShowDialog();
                LoadPrintersToDVG();
            }
        }

        private void dataGridView1_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex != -1 && e.ColumnIndex == 7)
            {
                DataGridViewRow row = dataGridView1.Rows[e.RowIndex];

                if (row.Cells[e.ColumnIndex].Value == null) return;

                toolTip = new ToolTip
                {
                    InitialDelay = 1500
                };

                if (Convert.ToBoolean(row.Cells["isPausedPrinter"].Value))
                {
                    toolTip.SetToolTip(dataGridView1, "Возобновить печать");
                    row.Cells[e.ColumnIndex].Value = Resources.play_hover;
                }
                else
                {
                    toolTip.SetToolTip(dataGridView1, "Приостановить печать");
                    row.Cells[e.ColumnIndex].Value = Resources.pause_hover;
                }
            }
        }

        private void dataGridView1_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex != -1 && e.ColumnIndex == 7)
            {
                DataGridViewRow row = dataGridView1.Rows[e.RowIndex];

                if (toolTip != null)
                    toolTip.Dispose();

                if (row.Cells[e.ColumnIndex].Value == null) return;

                if (Convert.ToBoolean(row.Cells["isPausedPrinter"].Value))
                {
                    row.Cells[e.ColumnIndex].Value = Resources.play;
                }
                else
                {
                    row.Cells[e.ColumnIndex].Value = Resources.pause;
                }
            }
        }

        private void filterSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new fmFilterSettings().ShowDialog();
            ReadPrinters();
            LoadPrintersToDVG();
        }
    }
}
