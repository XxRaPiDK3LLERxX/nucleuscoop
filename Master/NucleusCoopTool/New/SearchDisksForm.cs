﻿using Nucleus.Gaming;
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

namespace Nucleus.Coop
{
    public partial class SearchDisksForm : BaseForm
    {
        public struct SearchDriveInfo
        {
            public DriveInfo drive;
            public string text;

            public override string ToString()
            {
                return text;
            }
        }

        private List<SearchDriveInfo> toSearch;
        private float progress;
        private bool searching;
        private int done;
        private bool closed;
        private MainForm main;

        public SearchDisksForm(MainForm main)
        {
            this.main = main;
            InitializeComponent();

            DriveInfo[] drives = DriveInfo.GetDrives();
            CheckedListBox checkedBox = disksBox;

            for (int i = 0; i < drives.Length; i++)
            {
                DriveInfo drive = drives[i];

                SearchDriveInfo d = new SearchDriveInfo();
                d.drive = drive;

                if (drive.IsReady)
                {
                    if (drive.DriveFormat != "NTFS")
                    {
                        // ignore non-NTFS drives
                        continue;
                    }

                    try
                    {
                        long free = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
                        long total = drive.TotalSize / 1024 / 1024 / 1024;
                        long used = total - free;

                        d.text = drive.Name + " " + used + " GB used";
                        checkedBox.Items.Add(d, true);
                    }
                    catch
                    {
                        // notify user of crash
                        d.text = drive.Name + " (Not authorized)";
                        checkedBox.Items.Add(d, CheckState.Indeterminate);
                    }
                }
                else
                {
                    // user might want to get that drive ready
                    d.text = drive.Name + " (Drive not ready)";
                    checkedBox.Items.Add(d, CheckState.Indeterminate);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            closed = true;
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            if (searching)
            {
                return;
            }

            btnSearch.Enabled = false;
            searching = true;
            done = 0;

            toSearch = new List<SearchDriveInfo>();
            CheckedListBox checkedBox = disksBox;

            for (int i = 0; i < checkedBox.CheckedItems.Count; i++)
            {
                SearchDriveInfo info = (SearchDriveInfo)checkedBox.CheckedItems[i];
                toSearch.Add(info);
            }

            SearchDrives();
        }

        private void SearchDrives()
        {
            for (int i = 0; i < toSearch.Count; i++)
            {
                ThreadPool.QueueUserWorkItem(SearchDrive, i);
            }
        }

        private void UpdateProgress()
        {
            Invoke(new Action(delegate
            {
                progressBar1.Value = Math.Min(100, (int)(progress * 100));
            }));
        }

        private void SearchDrive(object state)
        {
            int i = (int)state;
            SearchDriveInfo info = toSearch[i];
            if (!info.drive.IsReady)
            {
                done++;
                return;
            }

            LogManager.Log("> Searching drive {0} for game executables", info.drive.Name);

            Dictionary<ulong, FileNameAndParentFrn> mDict = new Dictionary<ulong, FileNameAndParentFrn>();
            MFTReader mft = new MFTReader();
            mft.Drive = info.drive.RootDirectory.FullName;

            mft.EnumerateVolume(out mDict, new string[] { ".exe" });

            progress += (1 / (float)toSearch.Count) / 2.0f;
            UpdateProgress();

            float increment = (1 / (float)toSearch.Count) / (float)mDict.Count;
            foreach (KeyValuePair<UInt64, FileNameAndParentFrn> entry in mDict)
            {
                if (closed)
                {
                    return;
                }

                progress += increment;

                FileNameAndParentFrn file = (FileNameAndParentFrn)entry.Value;

                string name = file.Name;
                string lower = name.ToLower();

                if (GameManager.Instance.AnyGame(lower))
                {
                    string path = mft.GetFullPath(file);
                    if (path.Contains("$Recycle.Bin"))
                    {
                        // noope
                        continue;
                    }

                    UserGameInfo uinfo = GameManager.Instance.TryAddGame(path);

                    if (uinfo != null)
                    {
                        LogManager.Log("> Found new game {0} on drive {1}", uinfo.Game.GameName, info.drive.Name);
                        Invoke(new Action(delegate
                        {
                            listGames.Items.Add(uinfo.Game.GameName + " - " + path);
                            listGames.Invalidate();
                            main.NewUserGame(uinfo);
                        }));
                    }
                }
            }

            if (closed)
            {
                return;
            }
            UpdateProgress();

            done++;
            if (done == toSearch.Count)
            {
                searching = false;
                Invoke(new Action(delegate
                {
                    progress = 1;
                    UpdateProgress();
                    btnSearch.Enabled = true;
                    main.RefreshGames();
                    MessageBox.Show("Finished searching!");
                }));
            }
        }
    }
}