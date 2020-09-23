using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Diagnostics;
using System.Data;
using System.Text.RegularExpressions;
using System.Xml;
using System.Threading;
using System.Net;
using RemnantSaveManager.Properties;
using System.Windows.Markup;

namespace RemnantSaveManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static string defaultBackupFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Remnant\\Saved\\Backups";
        private static string backupDirPath;
        private static string saveDirPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\Remnant\\Saved\\SaveGames";
        private List<SaveBackup> listBackups;
        private Boolean suppressLog;
        private FileSystemWatcher saveWatcher;
        private Process gameProcess;

        //private List<RemnantCharacter> activeCharacters;
        private RemnantSave activeSave;

        private SaveAnalyzer activeSaveAnalyzer;
        private List<SaveAnalyzer> backupSaveAnalyzers;

        private System.Timers.Timer saveTimer;
        private DateTime lastUpdateCheck;
        private int saveCount;

        public enum LogType
        {
            Normal,
            Success,
            Error
        }

        private bool ActiveSaveIsBackedUp { 
            get {
                DateTime saveDate = File.GetLastWriteTime(saveDirPath + "\\profile.sav");
                for (int i = 0; i < listBackups.Count; i++)
                {
                    DateTime backupDate = listBackups.ToArray()[i].SaveDate;
                    if (saveDate.Equals(backupDate))
                    {
                        return true;
                    }
                }
                return false;
            } 
            set
            {
                if (value)
                {
                    lblStatus.ToolTip = "备份";
                    lblStatus.Content = FindResource("StatusOK");
                    btnBackup.IsEnabled = false;
                    btnBackup.Content = FindResource("SaveGrey");
                }
                else
                {
                    lblStatus.ToolTip = "不备份";
                    lblStatus.Content = FindResource("StatusNo");
                    btnBackup.IsEnabled = true;
                    btnBackup.Content = FindResource("Save");
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            suppressLog = false;
            txtLog.Text = "Version " + typeof(MainWindow).Assembly.GetName().Version;
            if (Properties.Settings.Default.CreateLogFile)
            {
                System.IO.File.WriteAllText("log.txt", DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
            }
            logMessage("加载中...");
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }

            if (Properties.Settings.Default.BackupFolder.Length == 0)
            {
                logMessage("未设置备份文件夹；正在还原为默认文件夹。");
                Properties.Settings.Default.BackupFolder = defaultBackupFolder;
                Properties.Settings.Default.Save();
            } 
            else if (!Directory.Exists(Properties.Settings.Default.BackupFolder) && !Properties.Settings.Default.BackupFolder.Equals(defaultBackupFolder))
            {
                logMessage("备份文件夹 ("+ Properties.Settings.Default.BackupFolder + ") 未找到; 正在还原默认值。");
                Properties.Settings.Default.BackupFolder = defaultBackupFolder;
                Properties.Settings.Default.Save();
            } 
            backupDirPath = Properties.Settings.Default.BackupFolder;
            txtBackupFolder.Text = backupDirPath;

            chkCreateLogFile.IsChecked = Properties.Settings.Default.CreateLogFile;

            saveTimer = new System.Timers.Timer();
            saveTimer.Interval = 2000;
            saveTimer.AutoReset = false;
            saveTimer.Elapsed += OnSaveTimerElapsed;

            saveWatcher = new FileSystemWatcher();
            saveWatcher.Path = saveDirPath;

            // Watch for changes in LastWrite times.
            saveWatcher.NotifyFilter = NotifyFilters.LastWrite;

            // Only watch sav files.
            saveWatcher.Filter = "profile.sav";

            // Add event handlers.
            saveWatcher.Changed += OnSaveFileChanged;
            saveWatcher.Created += OnSaveFileChanged;
            saveWatcher.Deleted += OnSaveFileChanged;
            //watcher.Renamed += OnRenamed;

            listBackups = new List<SaveBackup>();

            ((MenuItem)dataBackups.ContextMenu.Items[1]).Click += deleteMenuItem_Click;

            activeSaveAnalyzer = new SaveAnalyzer(this)
            {
                ActiveSave = true,
                Title = "分析当前存档已激活世界"
            };
            backupSaveAnalyzers = new List<SaveAnalyzer>();

            ((MenuItem)dataBackups.ContextMenu.Items[0]).Click += analyzeMenuItem_Click;

            GameInfo.GameInfoUpdate += OnGameInfoUpdate;
            dataBackups.CanUserDeleteRows = false;
            saveCount = 0;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtLog.IsReadOnly = true;
            logMessage("当前存档日期: " + File.GetLastWriteTime(saveDirPath + "\\profile.sav").ToString());
            //logMessage("Backups folder: " + backupDirPath);
            //logMessage("Save folder: " + saveDirPath);
            loadBackups();
            bool autoBackup = Properties.Settings.Default.AutoBackup;
            chkAutoBackup.IsChecked = autoBackup;
            txtBackupMins.Text = Properties.Settings.Default.BackupMinutes.ToString();
            txtBackupLimit.Text = Properties.Settings.Default.BackupLimit.ToString();
            chkShowPossibleItems.IsChecked = Properties.Settings.Default.ShowPossibleItems;
            chkAutoCheckUpdate.IsChecked = Properties.Settings.Default.AutoCheckUpdate;

            cmbMissingItemColor.Items.Add("Red");
            cmbMissingItemColor.Items.Add("White");
            if (Properties.Settings.Default.MissingItemColor.ToString().Equals("Red"))
            {
                cmbMissingItemColor.SelectedIndex = 0;
            } else
            {
                cmbMissingItemColor.SelectedIndex = 1;
            }
            cmbMissingItemColor.SelectionChanged += cmbMissingItemColorSelectionChanged;

            saveWatcher.EnableRaisingEvents = true;
            activeSave = new RemnantSave(saveDirPath);
            updateCurrentWorldAnalyzer();

            if (Properties.Settings.Default.AutoCheckUpdate)
            {
                checkForUpdate();
            }
        }

        private void loadBackups()
        {
            if (!Directory.Exists(backupDirPath))
            {
                logMessage("未找到备份文件夹，正在创建...");
                Directory.CreateDirectory(backupDirPath);
            }
            dataBackups.ItemsSource = null;
            listBackups.Clear();
            Dictionary<long, string> backupNames = getSavedBackupNames();
            Dictionary<long, bool> backupKeeps = getSavedBackupKeeps();
            string[] files = Directory.GetDirectories(backupDirPath);
            SaveBackup activeBackup = null;
            for (int i = 0; i < files.Length; i++)
            {
                if (RemnantSave.ValidSaveFolder(files[i]))
                {
                    SaveBackup backup = new SaveBackup(files[i]);
                    if (backupNames.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Name = backupNames[backup.SaveDate.Ticks];
                    }
                    if (backupKeeps.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Keep = backupKeeps[backup.SaveDate.Ticks];
                    }

                    if (backupActive(backup))
                    {
                        backup.Active = true;
                        activeBackup = backup;
                    }

                    backup.Updated += saveUpdated;

                    listBackups.Add(backup);
                }
            }
            dataBackups.ItemsSource = listBackups;
            logMessage("找到备份: " + listBackups.Count);
            if (listBackups.Count > 0)
            {
                logMessage("上次备份存档日期: " + listBackups[listBackups.Count - 1].SaveDate.ToString());
            }
            if (activeBackup != null)
            {
                dataBackups.SelectedItem = activeBackup;
            }
            ActiveSaveIsBackedUp = (activeBackup != null);
        }

        private void saveUpdated(object sender, UpdatedEventArgs args)
        {
            if (args.FieldName.Equals("Name"))
            {
                updateSavedNames();
            }
            else if (args.FieldName.Equals("Keep"))
            {
                updateSavedKeeps();
            }
        }

        private void loadBackups(Boolean verbose)
        {
            Boolean oldVal = suppressLog;
            suppressLog = !verbose;
            loadBackups();
            suppressLog = oldVal;
        }

        private Boolean backupActive(SaveBackup saveBackup)
        {
            if (DateTime.Compare(saveBackup.SaveDate, File.GetLastWriteTime(saveDirPath + "\\profile.sav")) == 0)
            {
                return true;
            }
            return false;
        }

        private DateTime getBackupDateTime(string backupFolder)
        {
            return File.GetLastWriteTime(backupDirPath + "\\" + backupFolder + "\\profile.sav");
        }

        public void logMessage(string msg)
        {
            logMessage(msg, Colors.White);
        }

        public void logMessage(string msg, LogType lt)
        {
            Color color = Colors.White;
            if (lt == LogType.Success)
            {
                color = Color.FromRgb(0, 200, 0);
            }
            else if (lt == LogType.Error)
            {
                color = Color.FromRgb(200, 0, 0);
            }
            logMessage(msg, color);
        }

        public void logMessage(string msg, Color color)
        {
            if (!suppressLog)
            {
                txtLog.Text = txtLog.Text + Environment.NewLine + DateTime.Now.ToString() + ": " + msg;
                lblLastMessage.Content = msg;
                lblLastMessage.Foreground = new SolidColorBrush(color);
                if (color.Equals(Colors.White))
                {
                    lblLastMessage.FontWeight = FontWeights.Normal;
                }
                else
                {
                    lblLastMessage.FontWeight = FontWeights.Bold;
                }
            }
            if (Properties.Settings.Default.CreateLogFile)
            {
                StreamWriter writer = System.IO.File.AppendText("log.txt");
                writer.WriteLine(DateTime.Now.ToString() + ": " + msg);
                writer.Close();
            }
        }

        private void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            doBackup();
        }

        private void doBackup()
        {
            try
            {
                int existingSaveIndex = -1;
                DateTime saveDate = File.GetLastWriteTime(saveDirPath + "\\profile.sav");
                string backupFolder = backupDirPath + "\\" + saveDate.Ticks;
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                } else if (RemnantSave.ValidSaveFolder(backupFolder))
                {
                    for (int i=listBackups.Count-1; i >= 0; i--)
                    {
                        if (listBackups[i].SaveDate.Ticks == saveDate.Ticks)
                        {
                            existingSaveIndex = i;
                            break;
                        }
                    }
                }
                foreach (string file in Directory.GetFiles(saveDirPath))
                    File.Copy(file, backupFolder + "\\" + System.IO.Path.GetFileName(file), true);
                if (RemnantSave.ValidSaveFolder(backupFolder))
                {
                    Dictionary<long, string> backupNames = getSavedBackupNames();
                    Dictionary<long, bool> backupKeeps = getSavedBackupKeeps();
                    SaveBackup backup = new SaveBackup(backupFolder);
                    if (backupNames.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Name = backupNames[backup.SaveDate.Ticks];
                    }
                    if (backupKeeps.ContainsKey(backup.SaveDate.Ticks))
                    {
                        backup.Keep = backupKeeps[backup.SaveDate.Ticks];
                    }
                    foreach (SaveBackup saveBackup in listBackups)
                    {
                        saveBackup.Active = false;
                    }
                    backup.Active = true;
                    backup.Updated += saveUpdated;
                    if (existingSaveIndex > -1)
                    {
                        listBackups[existingSaveIndex] = backup;
                    } else
                    {
                        listBackups.Add(backup);
                    }
                }
                checkBackupLimit();
                dataBackups.Items.Refresh();
                this.ActiveSaveIsBackedUp = true;
                logMessage($"备份已完成 ({saveDate.ToString()})!", LogType.Success);
            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    logMessage("保存正在使用的文件；等待0.5秒，然后重试。");
                    System.Threading.Thread.Sleep(500);
                    doBackup();
                }
            }
        }

        private Boolean isRemnantRunning()
        {
            Process[] pname = Process.GetProcessesByName("Remnant");
            if (pname.Length == 0)
            {
                return false;
            }
            return true;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (isRemnantRunning())
            {
                logMessage("在还原存档备份之前退出游戏。", LogType.Error);
                return;
            }

            if (dataBackups.SelectedItem == null)
            {
                logMessage("从列表中选择要还原的备份！", LogType.Error);
                return;
            }

            if (!this.ActiveSaveIsBackedUp)
            {
                doBackup();
            }

            saveWatcher.EnableRaisingEvents = false;
            System.IO.DirectoryInfo di = new DirectoryInfo(saveDirPath);
            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            SaveBackup selectedBackup = (SaveBackup)dataBackups.SelectedItem;
            foreach (var file in Directory.GetFiles(backupDirPath + "\\" + selectedBackup.SaveDate.Ticks))
                File.Copy(file, saveDirPath + "\\" + System.IO.Path.GetFileName(file));
            foreach (SaveBackup saveBackup in listBackups)
            {
                saveBackup.Active = false;
            }
            selectedBackup.Active = true;
            updateCurrentWorldAnalyzer();
            dataBackups.Items.Refresh();
            btnRestore.IsEnabled = false;
            btnRestore.Content = FindResource("RestoreGrey");
            logMessage("备份已还原！", LogType.Success);
            saveWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
        }

        private void ChkAutoBackup_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AutoBackup = chkAutoBackup.IsChecked.HasValue ? chkAutoBackup.IsChecked.Value : false;
            Properties.Settings.Default.Save();
        }

        private void OnSaveFileChanged(object source, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    //When the save files are modified, they are modified
                    //four times in relatively rapid succession.
                    //This timer is refreshed each time the save is modified,
                    //and a backup only occurs after the timer expires.
                    saveTimer.Interval = 10000;
                    saveTimer.Enabled = true;
                    saveCount++;
                    if (saveCount == 4)
                    {
                        updateCurrentWorldAnalyzer();
                        saveCount = 0;
                    }
                }
                catch (Exception ex)
                {
                    logMessage(ex.GetType()+" 设置保存文件定时: " +ex.Message+"("+ex.StackTrace+")");
                }
            });
        }

        private void OnSaveTimerElapsed(Object source, System.Timers.ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    //logMessage($"{DateTime.Now.ToString()} File: {e.FullPath} {e.ChangeType}");
                    if (Properties.Settings.Default.AutoBackup)
                    {
                        //logMessage($"Save: {File.GetLastWriteTime(e.FullPath)}; Last backup: {File.GetLastWriteTime(listBackups[listBackups.Count - 1].Save.SaveFolderPath + "\\profile.sav")}");
                        DateTime latestBackupTime;
                        DateTime newBackupTime;
                        if (listBackups.Count > 0)
                        {
                            latestBackupTime = listBackups[listBackups.Count - 1].SaveDate;
                            newBackupTime = latestBackupTime.AddMinutes(Properties.Settings.Default.BackupMinutes);
                        }
                        else
                        {
                            latestBackupTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                            newBackupTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                        }
                        if (DateTime.Compare(DateTime.Now, newBackupTime) >= 0)
                        {
                            doBackup();
                        }
                        else
                        {
                            this.ActiveSaveIsBackedUp = false;
                            foreach (SaveBackup backup in listBackups)
                            {
                                if (backup.Active) backup.Active = false;
                            }
                            dataBackups.Items.Refresh();
                            TimeSpan span = (newBackupTime - DateTime.Now);
                            logMessage($"已检测到存档更改, 距离下一次备份还有 {span.Minutes + Math.Round(span.Seconds / 60.0, 2)} 分钟");
                        }
                    }
                    if (saveCount != 0)
                    {
                        updateCurrentWorldAnalyzer();
                        saveCount = 0;
                    }

                    if (gameProcess == null || gameProcess.HasExited)
                    {
                        Process[] processes = Process.GetProcessesByName("Remnant");
                        if (processes.Length > 0)
                        {
                            gameProcess = processes[0];
                            gameProcess.EnableRaisingEvents = true;
                            gameProcess.Exited += (s, eargs) =>
                            {
                                this.Dispatcher.Invoke(() =>
                                {
                                    doBackup();
                                });
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage(ex.GetType() + " 正在处理存档文件更改: " + ex.Message + "(" + ex.StackTrace + ")");
                }
            });
        }

        private void TxtBackupMins_LostFocus(object sender, RoutedEventArgs e)
        {
            updateBackupMins();
        }

        private void TxtBackupMins_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                updateBackupMins();
            }
        }

        private void updateBackupMins()
        {
            string txt = txtBackupMins.Text;
            int mins;
            bool valid = false;
            if (txt.Length > 0)
            {
                if (int.TryParse(txt, out mins))
                {
                    valid = true;
                }
                else
                {
                    mins = Properties.Settings.Default.BackupMinutes;
                }
            }
            else
            {
                mins = Properties.Settings.Default.BackupMinutes;
            }
            if (mins != Properties.Settings.Default.BackupMinutes)
            {
                Properties.Settings.Default.BackupMinutes = mins;
                Properties.Settings.Default.Save();
            }
            if (!valid)
            {
                txtBackupMins.Text = Properties.Settings.Default.BackupMinutes.ToString();
            }
        }

        private void TxtBackupLimit_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                updateBackupLimit();
            }
        }

        private void TxtBackupLimit_LostFocus(object sender, RoutedEventArgs e)
        {
            updateBackupLimit();
        }

        private void updateBackupLimit()
        {
            string txt = txtBackupLimit.Text;
            int num;
            bool valid = false;
            if (txt.Length > 0)
            {
                if (int.TryParse(txt, out num))
                {
                    valid = true;
                }
                else
                {
                    num = Properties.Settings.Default.BackupLimit;
                }
            }
            else
            {
                num = 0;
            }
            if (num != Properties.Settings.Default.BackupLimit)
            {
                Properties.Settings.Default.BackupLimit = num;
                Properties.Settings.Default.Save();
            }
            if (!valid)
            {
                txtBackupLimit.Text = Properties.Settings.Default.BackupLimit.ToString();
            }
        }

        private void checkBackupLimit()
        {
            if (listBackups.Count > Properties.Settings.Default.BackupLimit && Properties.Settings.Default.BackupLimit > 0)
            {
                List<SaveBackup> removeBackups = new List<SaveBackup>();
                int delNum = listBackups.Count - Properties.Settings.Default.BackupLimit;
                for (int i = 0; i < listBackups.Count && delNum > 0; i++)
                {
                    if (!listBackups[i].Keep && !listBackups[i].Active)
                    {
                        logMessage("删除多余备份 " + listBackups[i].Name + " (" + listBackups[i].SaveDate + ")");
                        Directory.Delete(backupDirPath + "\\" + listBackups[i].SaveDate.Ticks, true);
                        removeBackups.Add(listBackups[i]);
                        delNum--;
                    }
                }

                for (int i=0; i < removeBackups.Count; i++)
                {
                    listBackups.Remove(removeBackups[i]);
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(backupDirPath))
            {
                logMessage("未找到备份文件夹，正在创建...");
                Directory.CreateDirectory(backupDirPath);
            }
            Process.Start(backupDirPath+"\\");
        }

        private Dictionary<long, string> getSavedBackupNames()
        {
            Dictionary<long, string> names = new Dictionary<long, string>();
            string savedString = Properties.Settings.Default.BackupName;
            string[] savedNames = savedString.Split(',');
            for (int i = 0; i < savedNames.Length; i++)
            {
                string[] vals = savedNames[i].Split('=');
                if (vals.Length == 2)
                {
                    names.Add(long.Parse(vals[0]), System.Net.WebUtility.UrlDecode(vals[1]));
                }
            }
            return names;
        }

        private Dictionary<long, bool> getSavedBackupKeeps()
        {
            Dictionary<long, bool> keeps = new Dictionary<long, bool>();
            string savedString = Properties.Settings.Default.BackupKeep;
            string[] savedKeeps = savedString.Split(',');
            for (int i = 0; i < savedKeeps.Length; i++)
            {
                string[] vals = savedKeeps[i].Split('=');
                if (vals.Length == 2)
                {
                    keeps.Add(long.Parse(vals[0]), bool.Parse(vals[1]));
                }
            }
            return keeps;
        }

        private void DataBackups_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Column.Header.ToString().Equals("SaveDate") || e.Column.Header.ToString().Equals("激活")) e.Cancel = true;
        }

        private void DataBackups_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString().Equals("Name") && e.EditAction == DataGridEditAction.Commit)
            {
                SaveBackup sb = (SaveBackup)e.Row.Item;
                if (sb.Name.Equals(""))
                {
                    sb.Name = sb.SaveDate.Ticks.ToString();
                }
            }
        }

        private void updateSavedNames()
        {
            List<string> savedNames = new List<string>();
            for (int i = 0; i < listBackups.Count; i++)
            {
                SaveBackup s = listBackups[i];
                if (!s.Name.Equals(s.SaveDate.Ticks.ToString()))
                {
                    savedNames.Add(s.SaveDate.Ticks + "=" + System.Net.WebUtility.UrlEncode(s.Name));
                }
                else
                {
                }
            }
            if (savedNames.Count > 0)
            {
                Properties.Settings.Default.BackupName = string.Join(",", savedNames.ToArray());
            }
            else
            {
                Properties.Settings.Default.BackupName = "";
            }
            Properties.Settings.Default.Save();
        }

        private void updateSavedKeeps()
        {
            List<string> savedKeeps = new List<string>();
            for (int i = 0; i < listBackups.Count; i++)
            {
                SaveBackup s = listBackups[i];
                if (s.Keep)
                {
                    savedKeeps.Add(s.SaveDate.Ticks + "=True");
                }
            }
            if (savedKeeps.Count > 0)
            {
                Properties.Settings.Default.BackupKeep = string.Join(",", savedKeeps.ToArray());
            }
            else
            {
                Properties.Settings.Default.BackupKeep = "";
            }
            Properties.Settings.Default.Save();
        }

        private void DataBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MenuItem analyzeMenu = ((MenuItem)dataBackups.ContextMenu.Items[0]);
            MenuItem deleteMenu = ((MenuItem)dataBackups.ContextMenu.Items[1]);
            if (e.AddedItems.Count > 0)
            {
                SaveBackup selectedBackup = (SaveBackup)(dataBackups.SelectedItem);
                if (backupActive(selectedBackup))
                {
                    btnRestore.IsEnabled = false;
                    btnRestore.Content = FindResource("RestoreGrey");
                }
                else
                {
                    btnRestore.IsEnabled = true;
                    btnRestore.Content = FindResource("Restore");
                }

                analyzeMenu.IsEnabled = true;
                deleteMenu.IsEnabled = true;
            }
            else
            {
                analyzeMenu.IsEnabled = false;
                deleteMenu.IsEnabled = false;
                btnRestore.IsEnabled = false;
                btnRestore.Content = FindResource("RestoreGrey");
            }
        }

        private void analyzeMenuItem_Click(object sender, System.EventArgs e)
        {
            SaveBackup saveBackup = (SaveBackup)dataBackups.SelectedItem;
            logMessage("显示备份存档 (" + saveBackup.Name + ") 世界分析...");
            SaveAnalyzer analyzer = new SaveAnalyzer(this);
            analyzer.Title = "备份存档 ("+saveBackup.Name+") 世界分析";
            analyzer.Closing += Backup_Analyzer_Closing;
            List<RemnantCharacter> chars = saveBackup.Save.Characters;
            for (int i = 0; i < chars.Count; i++)
            {
                chars[i].LoadWorldData(i);
            }
            analyzer.LoadData(chars);
            backupSaveAnalyzers.Add(analyzer);
            analyzer.Show();
        }

        private void Backup_Analyzer_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            backupSaveAnalyzers.Remove((SaveAnalyzer)sender);
        }

        private void deleteMenuItem_Click(object sender, System.EventArgs e)
        {
            SaveBackup save = (SaveBackup)dataBackups.SelectedItem;
            var confirmResult = MessageBox.Show("您确定要删除备份 \"" + save.Name + "\" (" + save.SaveDate.ToString() + ")?",
                                     "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmResult == MessageBoxResult.Yes)
            {
                if (save.Keep)
                {
                    confirmResult = MessageBox.Show("此备份已标记为保留。 您确定要删除备份吗 \"" + save.Name + "\" (" + save.SaveDate.ToString() + ")?",
                                     "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }
                if (save.Active)
                {
                    this.ActiveSaveIsBackedUp = false;
                }
                if (Directory.Exists(backupDirPath + "\\" + save.SaveDate.Ticks))
                {
                    Directory.Delete(backupDirPath + "\\" + save.SaveDate.Ticks, true);
                }
                listBackups.Remove(save);
                dataBackups.Items.Refresh();
                logMessage("备份 \"" + save.Name + "\" (" + save.SaveDate + ") 已删除.");
            }
        }

        private void BtnAnalyzeCurrent_Click(object sender, RoutedEventArgs e)
        {
            logMessage("显示当前存档世界分析...");
            activeSaveAnalyzer.Show();
        }

        private void updateCurrentWorldAnalyzer()
        {
            activeSave.UpdateCharacters();
            /*for (int i = 0; i < activeSave.Characters.Count; i++)
            {
                Console.WriteLine(activeSave.Characters[i]);
            }*/
            activeSaveAnalyzer.LoadData(activeSave.Characters);
        }

        private void OnGameInfoUpdate(object source, GameInfoUpdateEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                logMessage(e.Message);
                if (e.Result == GameInfoUpdateResult.Updated)
                {
                    updateCurrentWorldAnalyzer();
                }
            });
        }

        private void checkForUpdate()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                GameInfo.CheckForNewGameInfo();
            }).Start();
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                try
                {
                    WebClient client = new WebClient();
                    string source = client.DownloadString("https://github.com/Razzmatazzz/RemnantSaveManager/releases/latest");
                    string title = Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;
                    string remoteVer = Regex.Match(source, @"Remnant Save Manager (?<Version>([\d.]+)?)", RegexOptions.IgnoreCase).Groups["Version"].Value;

                    Version remoteVersion = new Version(remoteVer);
                    Version localVersion = typeof(MainWindow).Assembly.GetName().Version;

                    this.Dispatcher.Invoke(() =>
                    {
                        //do stuff in here with the interface
                        if (localVersion.CompareTo(remoteVersion) == -1)
                        {
                            var confirmResult = MessageBox.Show("检测新版本。 您想打开下载页面吗?",
                                     "有可用更新", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                            if (confirmResult == MessageBoxResult.Yes)
                            {
                                Process.Start("https://github.com/Razzmatazzz/RemnantSaveManager/releases/latest");
                                System.Environment.Exit(1);
                            }
                        } else
                        {
                            //logMessage("No new version found.");
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logMessage("检查新版本时出错: " + ex.Message, LogType.Error);
                    });
                }
            }).Start();
            lastUpdateCheck = DateTime.Now;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            /*activeSaveAnalyzer.ActiveSave = false;
            activeSaveAnalyzer.Close();
            for (int i = backupSaveAnalyzers.Count - 1; i > -1; i--)
            {
                backupSaveAnalyzers[i].Close();
            }*/
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            System.Environment.Exit(1);
        }

        private void BtnBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = backupDirPath;
            openFolderDialog.Description = "选择要备份存档的文件夹。";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(saveDirPath))
                {
                    MessageBox.Show("请选择游戏存档文件夹以外的文件夹。",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                if (folderName.Equals(backupDirPath))
                {
                    return;
                }
                if (listBackups.Count > 0)
                {
                    var confirmResult = MessageBox.Show("您是否要将备份移动到此新文件夹?",
                                     "移动备份", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (confirmResult == MessageBoxResult.Yes)
                    {
                        List<String> backupFiles = Directory.GetDirectories(backupDirPath).ToList();
                        foreach (string file in backupFiles)
                        {
                            if (File.Exists(file+@"\profile.sav"))
                            {
                                string subFolderName = file.Substring(file.LastIndexOf(@"\"));
                                Directory.CreateDirectory(folderName + subFolderName);
                                Directory.SetCreationTime(folderName + subFolderName, Directory.GetCreationTime(file));
                                Directory.SetLastWriteTime(folderName + subFolderName, Directory.GetCreationTime(file));
                                foreach (string filename in Directory.GetFiles(file))
                                {
                                    File.Copy(filename, filename.Replace(backupDirPath, folderName));
                                }
                                Directory.Delete(file, true);
                                //Directory.Move(file, folderName + subFolderName);
                            }
                        }
                    }
                }
                txtBackupFolder.Text = folderName;
                backupDirPath = folderName;
                Properties.Settings.Default.BackupFolder = folderName;
                Properties.Settings.Default.Save();
                loadBackups();
            }
        }

        private void DataBackups_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column.Header.Equals("Save")) {
                e.Cancel = true;
            }
        }

        private void btnGameInfoUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (lastUpdateCheck.AddMinutes(10) < DateTime.Now)
            {
                checkForUpdate();
            }
            else
            {
                TimeSpan span = (lastUpdateCheck.AddMinutes(10) - DateTime.Now);
                logMessage("请稍候 " + span.Minutes+" 分钟, "+span.Seconds+" 检查更新前的秒数.");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            //need to call twice for some reason
            dataBackups.CancelEdit();
            dataBackups.CancelEdit();
        }
        private void menuRestoreWorlds_Click(object sender, RoutedEventArgs e)
        {
            if (isRemnantRunning())
            {
                logMessage("在还原存档备份之前退出游戏.", LogType.Error);
                return;
            }

            if (dataBackups.SelectedItem == null)
            {
                logMessage("从列表中选择要还原的备份!", LogType.Error);
                return;
            }

            SaveBackup selectedBackup = (SaveBackup)dataBackups.SelectedItem;
            if (selectedBackup.Save.Characters.Count != activeSave.Characters.Count)
            {
                logMessage("备份字符数与当前字符数不匹配.");
                MessageBoxResult confirmResult = MessageBox.Show("激活的存档的字符数与要还原的备份世界的字符数不同。 这可能会导致意外问题。 继续?",
                                     "字符不匹配", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirmResult == MessageBoxResult.No)
                {
                    logMessage("取消世界还原.");
                    return;
                }
            }

            if (!this.ActiveSaveIsBackedUp)
            {
                doBackup();
            }

            saveWatcher.EnableRaisingEvents = false;
            System.IO.DirectoryInfo di = new DirectoryInfo(saveDirPath);
            foreach (FileInfo file in di.GetFiles("save_?.sav"))
            {
                file.Delete();
            }
            di = new DirectoryInfo(backupDirPath + "\\" + selectedBackup.SaveDate.Ticks);
            foreach (FileInfo file in di.GetFiles("save_?.sav"))
                File.Copy(file.FullName, saveDirPath + "\\" + file.Name);
            foreach (SaveBackup saveBackup in listBackups)
            {
                saveBackup.Active = false;
            }
            File.SetLastWriteTime(saveDirPath + "\\profile.sav", DateTime.Now);
            updateCurrentWorldAnalyzer();
            dataBackups.Items.Refresh();
            this.ActiveSaveIsBackedUp = false;
            btnBackup.IsEnabled = false;
            btnBackup.Content = FindResource("SaveGrey");
            logMessage("备份世界数据已还原!", LogType.Success);
            saveWatcher.EnableRaisingEvents = Properties.Settings.Default.AutoBackup;
        }

        private void chkCreateLogFile_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkCreateLogFile.IsChecked.HasValue ? chkCreateLogFile.IsChecked.Value : false;
            if (newValue & !Properties.Settings.Default.CreateLogFile)
            {
                System.IO.File.WriteAllText("log.txt", DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
            }
            Properties.Settings.Default.CreateLogFile = newValue;
            Properties.Settings.Default.Save();
        }

        private void cmbMissingItemColorSelectionChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.MissingItemColor = cmbMissingItemColor.SelectedItem.ToString();
            Properties.Settings.Default.Save();
            updateCurrentWorldAnalyzer();
        }

        private void chkShowPossibleItems_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkShowPossibleItems.IsChecked.HasValue ? chkShowPossibleItems.IsChecked.Value : false;
            Properties.Settings.Default.ShowPossibleItems = newValue;
            Properties.Settings.Default.Save();
            updateCurrentWorldAnalyzer();
        }

        private void chkAutoCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkAutoCheckUpdate.IsChecked.HasValue ? chkAutoCheckUpdate.IsChecked.Value : false;
            Properties.Settings.Default.AutoCheckUpdate = newValue;
            Properties.Settings.Default.Save();
        }
    }
}
