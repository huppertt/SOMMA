using System;
using Gtk;
using System.Net.Sockets;
using System.Text;
using System.Threading;
//using MathNet.Filtering;
using System.Collections.Generic;
using csmatio.io;
using csmatio.types;
using System.Xml;
using System.IO;

public partial class MainWindow : Gtk.Window
{
    UdpClient udpClient;
    bool isrunning;
    public static Thread mainthread;
//    public OnlineFilter EMGfilter;

    private static int MAXLISTLEN = 7200; // 10Hz * 12min

    public List<double>[] MyData;
    public List<double> events;

    public string MyDataFolder;

    public int udpport;
    public string udpaddress;
    int scanNumber;
    public string SubjID;
    public string ScanName;
    public string Comments;


    public MainWindow() : base(Gtk.WindowType.Toplevel)
    {
        Build();

        this.DeleteEvent += CloseProgram;


        // read the config file

        XmlDocument doc = new XmlDocument();
        XmlDocument doc2 = new XmlDocument();
        doc.Load(@"Config.xml");
        XmlNodeList elemList;

        elemList = doc.GetElementsByTagName("datadir");
        MyDataFolder = elemList[0].InnerXml;
        MyDataFolder = MyDataFolder.Trim();

        elemList = doc.GetElementsByTagName("udpaddress");
        udpaddress = elemList[0].InnerXml;
        udpaddress = udpaddress.Trim();

        elemList = doc.GetElementsByTagName("udpport");
        udpport = Convert.ToInt32(elemList[0].InnerXml);

        scanNumber = 1;

        // {time-EMG,EMG,EMG-FILTER TIME-NIRS,RAW1-4A,RAW1-4B,DOD-A,DOD-B,HBO,HBR,HBT,SO2}
        // {time-EMG,EMG,TIME-NIRS,RAW1-4A,RAW1-4B,DOD-A,DOD-B,HBO,HBR,HBT,SO2}
        MyData = new List<double>[17];
        MyData[0] = new List<double>(MAXLISTLEN*50);
        MyData[1] = new List<double>(MAXLISTLEN * 50);
        for (int i = 2; i < 17; i++)
        {
            MyData[i] = new List<double>(MAXLISTLEN);
        }

        events = new List<double>();

        drawingarea1.ExposeEvent += MyDataDraw;

        udpClient = new UdpClient(udpport);
        udpClient.Connect(udpaddress, udpport);

        // Check for the device
        Byte[] sendBytes2 = Encoding.ASCII.GetBytes("IDN?");
        udpClient.Send(sendBytes2, sendBytes2.Length);
        Thread.Sleep(500);
        if (udpClient.Available>0) {
            System.Net.IPEndPoint iPEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            byte[] msg = udpClient.Receive(ref iPEndPoint);
            string msgs = Encoding.ASCII.GetString(msg);
            msgs = msgs.Substring(0, msgs.IndexOf("\r"));
            StatusBarLabel1.Text = String.Format("Connected to {0} at {1}", msgs, udpaddress);
            entrySUbjID.Sensitive = true;
            entryScanName.Sensitive = true;
        }
        else
        {
            StatusBarLabel1.ModifyFg(StateType.Normal, new Gdk.Color(255,0,0));
            StatusBarLabel1.Text = String.Format("NO DEVICE FOUND at {0}",udpaddress);
            entrySUbjID.Sensitive = false;
            entryScanName.Sensitive = false;
            udpClient.Close();
        }

        isrunning = false;
        mainthread = new Thread(updateData);

        //double Fs = 425;

        //EMGfilter = OnlineFilter.CreateBandpass(ImpulseResponse.Finite, Fs, 20, 400);
        
    }

    protected void OnDeleteEvent(object sender, DeleteEventArgs a)
    {
        Application.Quit();
        a.RetVal = true;
    }

    protected void ClickedStart(object sender, EventArgs e)
    {

        if (!isrunning)
        {

            MyData = new List<double>[17];
            MyData[0] = new List<double>(MAXLISTLEN*50);
            MyData[1] = new List<double>(MAXLISTLEN*50);
            for (int i = 2; i < 17; i++)
            {
                MyData[i] = new List<double>(MAXLISTLEN);
            }

            events = new List<double>();

            Byte[] sendBytes2 = Encoding.ASCII.GetBytes("IDN?");
            udpClient.Send(sendBytes2, sendBytes2.Length);
            System.Net.IPEndPoint iPEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            byte[] msg = udpClient.Receive(ref iPEndPoint);
            string msgs = Encoding.ASCII.GetString(msg);

            Byte[] sendBytes = Encoding.ASCII.GetBytes("Start");
            udpClient.Send(sendBytes, sendBytes.Length);
            mainthread = new Thread(updateData);

            mainthread.Start();
            isrunning = true;
            buttonStart.Label = "Stop";


        }
        else
        {
            Byte[] sendBytes = Encoding.ASCII.GetBytes("Stop");
            udpClient.Send(sendBytes, sendBytes.Length);
            mainthread.Abort();
            isrunning = false;
            buttonStart.Label = "Start";

            //save the MyData
            string[] paths = new string[] {MyDataFolder,
                String.Format("{0}",entrySUbjID.Text) };
           string pathname = System.IO.Path.Combine(paths);

            if (!Directory.Exists(pathname))
            {
                Directory.CreateDirectory(pathname);
            }

            string filename = String.Format("{0}_{1}_{2}.mat",entrySUbjID.Text,entryScanName.Text,DateTime.Now.ToString("MMMMddyyyy_HHmm"));
            filename = System.IO.Path.Combine(pathname, filename);
            SaveMyData(filename);

            scanNumber++;
            entryScanName.Text = String.Format("Scan-{0}", scanNumber);
            StatusBarLabel2.Text = string.Format("{0}-{1}-{2}", SubjID, "<date>", entryScanName.Text);
        }


    }

    public void SaveMyData(string filename)
    {
        // Store the MyData into the *.mat matlab format

        MLStructure mlhdr = new MLStructure("hdr", new int[] { 1, 1 });

        mlhdr["Date"] = new MLChar("",DateTime.Now.ToString("MMMMddyyyy_HHmm"));
        double[] wavelengths = new double[2];
        wavelengths[0] = 660;
        wavelengths[1] = 850;
        mlhdr["Wavelengths"] = new MLDouble("", wavelengths, 1);
        mlhdr["SubjID"] = new MLChar("", entrySUbjID.Text);
        mlhdr["Scan"] = new MLChar("", entryScanName.Text);
        mlhdr["Comments"] = new MLChar("", textview1.Buffer.Text);



        // save the raw MyData info
        int n2 = MyData[0].Count;
        int n =  MyData[3].Count;



        double[] _time = new double[n];
        double[] _Det1a = new double[n];
        double[] _Det2a = new double[n];
        double[] _Det3a = new double[n];
        double[] _Det4a = new double[n];
        double[] _Det1b = new double[n];
        double[] _Det2b = new double[n];
        double[] _Det3b = new double[n];
        double[] _Det4b = new double[n];

        double[] _timeEMG = new double[n2];
        double[] _EMG = new double[n2];
        //double[] _EMG_filter = new double[n2];

        double[] _HbO = new double[n];
        double[] _HbR = new double[n];
        double[] _HbT = new double[n];
        double[] _SO2 = new double[n];
        double[] _dODa = new double[n];
        double[] _dODb = new double[n];

        for (int i = 0; i < n2; i++)
        {
            _timeEMG[i] = MyData[0][i];
            _EMG[i] =     MyData[1][i];
            //_EMG_filter[i] = MyData[2][i];
        }

        for (int i=0; i<n; i++)
        {
            _time[i] =  MyData[2][i];
            _Det1a[i] = MyData[3][i];
            _Det2a[i] = MyData[4][i];
            _Det3a[i] = MyData[5][i];
            _Det4a[i] = MyData[6][i];

            _Det1b[i] = MyData[7][i];
            _Det2b[i] = MyData[8][i];
            _Det3b[i] = MyData[9][i];
            _Det4b[i] = MyData[10][i];

            _dODa[i] = MyData[11][i];
            _dODb[i] = MyData[12][i];
        
            _HbO[i] = MyData[13][i];
            _HbR[i] = MyData[14][i];
            _HbT[i] = MyData[15][i];
            _SO2[i] = MyData[16][i];
        }


        List<MLArray> mlList = new List<MLArray>();

        mlList.Add(new MLDouble("time_NIRS", _time, 1));

        mlList.Add(new MLDouble("Det1_660nm", _Det1a, 1));
        mlList.Add(new MLDouble("Det1_660nm", _Det2a, 1));
        mlList.Add(new MLDouble("Det1_660nm", _Det3a, 1));
        mlList.Add(new MLDouble("Det1_660nm", _Det4a, 1));

        mlList.Add(new MLDouble("Det1_850nm", _Det1b, 1));
        mlList.Add(new MLDouble("Det1_850nm", _Det2b, 1));
        mlList.Add(new MLDouble("Det1_850nm", _Det3b, 1));
        mlList.Add(new MLDouble("Det1_850nm", _Det4b, 1));

        mlList.Add(new MLDouble("dOD_660nm", _dODa, 1));
        mlList.Add(new MLDouble("dOD_850nm", _dODb, 1));

        mlList.Add(new MLDouble("HbO", _HbO, 1));
        mlList.Add(new MLDouble("HbR", _HbR, 1));
        mlList.Add(new MLDouble("HbT", _HbT, 1));
        mlList.Add(new MLDouble("SO2", _SO2, 1));

        mlList.Add(new MLDouble("time_EMG", _timeEMG, 1));
        mlList.Add(new MLDouble("EMG", _EMG, 1));
       // mlList.Add(new MLDouble("EMG_filtered", _EMG_filter, 1));


        mlList.Add(mlhdr);
        
        new MatFileWriter(filename, mlList, false);

        return;
    }


    protected void LED660changed(object sender, EventArgs e)
    {
        int val = (int)spinbutton1.Value;

        Byte[] sendBytes = Encoding.ASCII.GetBytes(String.Format("LED1 [0]", val));
        udpClient.Send(sendBytes, sendBytes.Length);
    }

    protected void LED850changed(object sender, EventArgs e)
    {
        int val = (int)spinbutton3.Value;

        Byte[] sendBytes = Encoding.ASCII.GetBytes(String.Format("LED2 [0]", val));
        udpClient.Send(sendBytes, sendBytes.Length);

    }

    protected void updateData()
    {
        System.Net.IPEndPoint iPEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
        // "10.0.0.1", 2390

  

        while (mainthread.IsAlive)
        {

            try
            {
  
                    byte[] msg = udpClient.Receive(ref iPEndPoint);
                    string msgs = Encoding.ASCII.GetString(msg);
                    string[] msgss = msgs.Split(new char[] { ' ' });

                    MyData[0].Add(Convert.ToDouble(msgss[0]) / 1000);  // time-EMG
                    MyData[1].Add(Convert.ToDouble(msgss[1]));  // EMG
                                                                //    MyData[2].Add(EMGfilter.ProcessSample(Convert.ToDouble(msgss[1]))); // EMG-filtered


                    if (msgss.Length > 3)
                    {
                        MyData[2].Add(Convert.ToDouble(msgss[0]) / 1000);  // time-NIRS
                        MyData[3].Add(Convert.ToDouble(msgss[2]));  // Det1a
                        MyData[4].Add(Convert.ToDouble(msgss[3]));  // Det2a
                        MyData[5].Add(Convert.ToDouble(msgss[4]));  // Det3a
                        MyData[6].Add(Convert.ToDouble(msgss[5]));  // Det4a

                        MyData[7].Add(Convert.ToDouble(msgss[6]));  // Det1b
                        MyData[8].Add(Convert.ToDouble(msgss[7]));  // Det2b
                        MyData[9].Add(Convert.ToDouble(msgss[8]));  // Det3b
                        MyData[10].Add(Convert.ToDouble(msgss[9]));  // Det4b

                        MyData[11].Add(Convert.ToDouble(msgss[10]));  // dODa
                        MyData[12].Add(Convert.ToDouble(msgss[11]));  // dODb

                        MyData[13].Add(Convert.ToDouble(msgss[12]));  // HbO
                        MyData[14].Add(Convert.ToDouble(msgss[13]));  // HbR
                        MyData[15].Add(Convert.ToDouble(msgss[14]));  // HbT
                        MyData[16].Add(Convert.ToDouble(msgss[15]));  // SO2

                        drawingarea1.QueueDraw();
                        progressbar1.Pulse();
                    
                    }
                
            }
            catch
            {
              
            }
        }
    }

    protected void MyDataDraw(object sender,EventArgs e)
    {


        try
        {

            Gdk.Drawable da = drawingarea1.GdkWindow;




            int selected = combobox5.Active;
            int MyDatasel = 1;
            int timesel = 1;


            switch (selected)
            {
                case 0:  // EMG-filter
                    MyDatasel = 1;
                    timesel = 0;
                    break;
                case 1:  //HbO
                    MyDatasel = 13;
                    timesel = 2;
                    break;
                case 2: //HbR
                    MyDatasel = 14;
                    timesel = 2;
                    break;
                case 3: //HbT
                    MyDatasel = 15;
                    timesel = 2;
                    break;
                case 4: //SO2
                    MyDatasel = 16;
                    timesel = 2;
                    break;
                case 5: // dOD 660
                    MyDatasel = 11;
                    timesel = 2;
                    break;
                case 6: //dOD 850
                    MyDatasel = 12;
                    timesel = 2;
                    break;
                case 7:  // NIRS raw
                    MyDatasel = 3;
                    timesel = 2;
                    break;
                case 8:  // NIRS raw
                    MyDatasel = 4;
                    timesel = 2;
                    break;
                case 9:  // NIRS raw
                    MyDatasel = 5;
                    timesel = 2;
                    break;
                case 10:  // NIRS raw
                    MyDatasel = 6;
                    timesel = 2;
                    break;
                case 11:  // NIRS raw
                    MyDatasel = 7;
                    timesel = 2;
                    break;
                case 12:  // NIRS raw
                    MyDatasel = 8;
                    timesel = 2;
                    break;
                case 13:  // NIRS raw
                    MyDatasel = 9;
                    timesel = 2;
                    break;
                case 14:  // NIRS raw
                    MyDatasel = 10;
                    timesel = 2;
                    break;

            }


            if (MyData[MyDatasel].Count < 2)
            {
                return;
            }


            double tMin = 0;
            if (checkbuttonWIndowTime.Active)
            {
                tMin = MyData[timesel][MyData[timesel].Count - 1] - Convert.ToDouble(entrywindowTime.Text);
            }


            int width, height;
            da.GetSize(out width, out height);

            int StartIdx = 0;


            for (int sIdx = 0; sIdx < MyData[timesel].Count; sIdx++)
            {
                if (MyData[timesel][sIdx] <= tMin)
                {
                    StartIdx = sIdx;
                }
            }

            double maxY, minY;
            minY = 99999; maxY = -99999;

            for (int tIdx = StartIdx; tIdx < MyData[MyDatasel].Count; tIdx++)
            {
                double d = MyData[MyDatasel][tIdx];
                if (maxY < d)
                {
                    maxY = d;
                }
                if (minY > d)
                {
                    minY = d;
                }
            }


            double rangeY = maxY - minY;
            double rangeX = MyData[timesel][(int)MyData[timesel].Count - 1] - MyData[timesel][StartIdx];
            int xoffset = 50;
            int yoffset = 1;
            height = height - 31;
            width = width - 51;

            Gdk.GC gc = new Gdk.GC(da);
            gc.RgbBgColor = new Gdk.Color(0, 0, 0);
            gc.RgbFgColor = new Gdk.Color(0, 0, 0);
            Gdk.Rectangle rarea = new Gdk.Rectangle();
            rarea.X = xoffset - 1;
            rarea.Y = yoffset - 1;
            rarea.Height = height + 2;
            rarea.Width = width + 2;
            da.DrawRectangle(gc, true, rarea);

            gc.RgbBgColor = new Gdk.Color(0, 0, 0);
            gc.RgbFgColor = new Gdk.Color(255, 255, 255);
            rarea.X = xoffset;
            rarea.Y = yoffset;
            rarea.Height = height;
            rarea.Width = width;
            da.DrawRectangle(gc, true, rarea);

            gc.RgbFgColor = new Gdk.Color(255, 0, 0);
            gc.SetLineAttributes(1, Gdk.LineStyle.Solid, Gdk.CapStyle.Projecting, Gdk.JoinStyle.Round);
            for (int tIdx = StartIdx + 1; tIdx < MyData[MyDatasel].Count; tIdx++)
            {
                double y2 = (MyData[MyDatasel][tIdx] - minY) / rangeY * height;
                double y1 = (MyData[MyDatasel][tIdx - 1] - minY) / rangeY * height;

                double x2 = (MyData[timesel][tIdx] - MyData[timesel][StartIdx]) / rangeX * width;
                double x1 = (MyData[timesel][tIdx - 1] - MyData[timesel][StartIdx]) / rangeX * width;

                da.DrawLine(gc, (int)x1 + xoffset, (int)(height - y1 + yoffset), (int)x2 + xoffset, (int)(height - y2 + yoffset));

            }

            gc.RgbFgColor = new Gdk.Color(0, 255, 0);
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] > MyData[timesel][StartIdx])
                {
                    double y2 = -1;
                    double y1 = height + 1;

                    double x2 = (events[i] - MyData[timesel][StartIdx]) / rangeX * width;
                    double x1 = (events[i] - MyData[timesel][StartIdx]) / rangeX * width;

                    da.DrawLine(gc, (int)x1 + xoffset, (int)(height - y1 + yoffset), (int)x2 + xoffset, (int)(height - y2 + yoffset));
                }
            }


            gc.RgbFgColor = new Gdk.Color(0, 0, 0);
            int numxlabels = 10;
            int numylabels = 5;
            double tstart, tend, dt;
            tstart = MyData[timesel][StartIdx];
            tend = MyData[timesel][MyData[timesel].Count - 1];
            dt = Math.Round((tend - tstart) / (1 + numxlabels));
            if (dt < 1)
            {
                dt = 1;
            }
            for (double i = 0; i < rangeX; i += dt)
            {
                double x = i / rangeX * width;
                Gtk.Label lab = new Label();
                lab.Text = String.Format("{0}", Math.Round((i + tstart) * 10) / 10);
                da.DrawLayout(gc, (int)x + xoffset, (int)height + 2, lab.Layout);
            }

            double dy;
            dy = rangeY / (1 + numylabels);
            if (dy == 0.0)
            {
                dy = 1;
            }
            for (double i = 0; i < rangeY; i += dy)
            {
                double y = height - i / rangeY * height;
                Gtk.Label lab = new Label();
                lab.Text = String.Format("{0}", Math.Round((i + minY) * 10) / 10);
                da.DrawLayout(gc, 10, (int)y + yoffset, lab.Layout);
            }
        }
        catch
        {
            Console.WriteLine("Draw error");
        }

    }

    protected void ChangeMyDataView(object sender, EventArgs e)
    {
    }

    protected void MarkStimulus(object sender, EventArgs e)
    {

        if (isrunning)
        {
            events.Add(MyData[0][MyData[0].Count - 1]);

            labelEvents.Text = String.Format("# events {0}", events.Count);
        }


    }

    protected void ExitProgram(object sender, EventArgs e)
    {
        udpClient.Close();
        Application.Quit();
    }

    protected void ConnectDevice(object sender, EventArgs e)
    {
        udpClient = new UdpClient(udpport);
        udpClient.Connect(udpaddress, udpport);

        // Check for the device
        Byte[] sendBytes2 = Encoding.ASCII.GetBytes("IDN?");
        udpClient.Send(sendBytes2, sendBytes2.Length);
        if (udpClient.Available > 0)
        {
            System.Net.IPEndPoint iPEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            byte[] msg = udpClient.Receive(ref iPEndPoint);
            string msgs = Encoding.ASCII.GetString(msg);
            StatusBarLabel1.Text = String.Format("Connected to {0} at {1}", msgs, udpaddress);
            entrySUbjID.Sensitive = true;
            entryScanName.Sensitive = true;
        }
        else
        {
            StatusBarLabel1.ModifyFg(StateType.Normal, new Gdk.Color(255, 0, 0));
            StatusBarLabel1.Text = String.Format("NO DEVICE FOUND at {0}", udpaddress);
            entrySUbjID.Sensitive = false;
            entryScanName.Sensitive = false;
        }
    }

    protected void EnterSubjID(object sender, EventArgs e)
    {
        SubjID = entrySUbjID.Text;
        ScanName = entryScanName.Text;
        buttonStart.Sensitive = true;

        StatusBarLabel2.Text = string.Format("{0}-{1}-{2}", SubjID, "<date>", ScanName);

    }

    protected void EnterScanName(object sender, EventArgs e)
    {
        ScanName = entryScanName.Text;
        StatusBarLabel2.Text = string.Format("{0}-{1}-{2}", SubjID, "<date>", ScanName);
    }

    protected void CloseProgram(object obj, DeleteEventArgs args)
    {
        
        udpClient.Close();
        Application.Quit();
    }
}
