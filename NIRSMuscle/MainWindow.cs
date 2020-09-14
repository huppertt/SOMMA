using System;
using Gtk;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MathNet.Filtering;
using MathNet.Filtering.Kalman;
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
    public OnlineFilter[] LowPassFilters;
    public OnlineFilter[] LowPassFilters2;
    public OnlineFilter[] NotchFilters;
    public OnlineFilter EMGfilter;

    public List<double>[] Data;
    public List<double>[] RawData;

    public List<double> state1;
    public List<double> state2;
    public List<double> time;
    public List<double> events;

    public string DataFolder;
    public double[] Calibration660;
    public double[] Calibration850;
    public double[] distances;

    public int udpport;
    public string udpaddress;

    public MainWindow() : base(Gtk.WindowType.Toplevel)
    {
        Build();


        // read the config file
       
        XmlDocument doc = new XmlDocument();
        XmlDocument doc2 = new XmlDocument();
        doc.Load(@"Config.xml");
        XmlNodeList elemList;

        elemList = doc.GetElementsByTagName("datadir");
        DataFolder = elemList[0].InnerXml;
        DataFolder = DataFolder.Trim();

        elemList = doc.GetElementsByTagName("udpaddress");
        udpaddress = elemList[0].InnerXml;
        udpaddress = udpaddress.Trim();

        elemList = doc.GetElementsByTagName("udpport");
        udpport = Convert.ToInt32(elemList[0].InnerXml);
        

        elemList = doc.GetElementsByTagName("calibration660");
        Calibration660 = new double[elemList.Count];
        for (int i = 0; i < elemList.Count; i++)
        {
            Calibration660[i] = Convert.ToDouble(elemList[i].InnerXml);
        }

        
        elemList = doc.GetElementsByTagName("calibration850");
        Calibration850 = new double[elemList.Count];
        for (int i = 0; i < elemList.Count; i++)
        {
            Calibration850[i] = Convert.ToDouble(elemList[i].InnerXml);
        }

        elemList = doc.GetElementsByTagName("distances");
        distances = new double[elemList.Count];
        for (int i = 0; i < elemList.Count; i++)
        {
            distances[i] = Convert.ToDouble(elemList[i].InnerXml);
        }


        Data = new List<double>[16];
        RawData = new List<double>[8];
        for (int i = 0; i < 16; i++)
        {
            Data[i] = new List<double>();
        }
        for (int i = 0; i < 8; i++)
        {
            RawData[i] = new List<double>();
        }

        state1 = new List<double>();
        state2 = new List<double>();
        time = new List<double>();
        events = new List<double>();

        drawingarea1.ExposeEvent += DataDraw;

        udpClient = new UdpClient(udpport);
        udpClient.Connect(udpaddress, udpport);
        isrunning = false;
        mainthread = new Thread(updateData);

        double Fs = 425;

        LowPassFilters = new OnlineFilter[8];
        for (int i = 0; i < 8; i++)
        {
            LowPassFilters[i] = OnlineFilter.CreateLowpass(ImpulseResponse.Finite, Fs, 10);
            LowPassFilters[i].Reset();
        }
        EMGfilter = OnlineFilter.CreateBandpass(ImpulseResponse.Finite, Fs, 20, 400);


        LowPassFilters2 = new OnlineFilter[2];
        LowPassFilters2[0] = OnlineFilter.CreateLowpass(ImpulseResponse.Finite, Fs, 10);
        LowPassFilters2[0].Reset();
        LowPassFilters2[1] = OnlineFilter.CreateLowpass(ImpulseResponse.Finite, Fs, 10);
        LowPassFilters2[1].Reset();


        NotchFilters = new OnlineFilter[5];
        for (int i = 0; i < 5; i++)
        {
            NotchFilters[i] = OnlineFilter.CreateBandstop(ImpulseResponse.Finite, Fs, 55, 65);
        }

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

            Data = new List<double>[16];
            for (int i = 0; i < 16; i++)
            {
                Data[i] = new List<double>();
            }
            for (int i = 0; i < 8; i++)
            {
                RawData[i] = new List<double>();
            }

            state1 = new List<double>();
            state2 = new List<double>();
            time = new List<double>();
            events = new List<double>();


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

            //save the data
            string[] paths = new string[] {DataFolder,
                String.Format("{0}",entrySUbjID.Text) };
           string pathname = System.IO.Path.Combine(paths);

            if (!Directory.Exists(pathname))
            {
                Directory.CreateDirectory(pathname);
            }

            string filename = String.Format("{0}_{1}_{2}.mat",entrySUbjID.Text,entryScanName.Text,DateTime.Now.ToString("MMMMddyyyy_HHmm"));
            filename = System.IO.Path.Combine(pathname, filename);
            SaveData(filename);

        }


    }

    public void SaveData(string filename)
    {
        // Store the data into the *.mat matlab format

        MLStructure mlhdr = new MLStructure("hdr", new int[] { 1, 1 });

        mlhdr["Date"] = new MLChar("",DateTime.Now.ToString("MMMMddyyyy_HHmm"));
        mlhdr["Calibration_660nm"] = new MLDouble("", Calibration660, 1);
        mlhdr["Calibration_850nm"] = new MLDouble("", Calibration850, 1);
        mlhdr["Distances"] = new MLDouble("", distances, 1);
        double[] wavelengths = new double[2];
        wavelengths[0] = 660;
        wavelengths[1] = 850;
        mlhdr["Wavelengths"] = new MLDouble("", wavelengths, 1);
        mlhdr["SubjID"] = new MLChar("", entrySUbjID.Text);
        mlhdr["Scan"] = new MLChar("", entryScanName.Text);
        mlhdr["Comments"] = new MLChar("", textview1.Buffer.Text);



        // save the raw data info

        MLStructure mlraw = new MLStructure("rawData", new int[] { 1, 1 });
        double[] state1 = new double[RawData[0].Count];
        double[] state2 = new double[RawData[1].Count];
        double[] _time = new double[  RawData[2].Count];
        double[] Det1 = new double[  RawData[3].Count];
        double[] Det2 = new double[  RawData[4].Count];
        double[] Det3 = new double[  RawData[5].Count];
        double[] Det4 = new double[  RawData[6].Count];
        double[] EMG = new double[   RawData[7].Count];

        for (int i=0; i< RawData[0].Count; i++)
        {
            _time[i] = time[i];
            state1[i] = RawData[0][i];
            state2[i] = RawData[1][i];
            time[i] =   RawData[2][i];
            Det1[i] =   RawData[3][i];
            Det2[i] =   RawData[4][i];
            Det3[i] =   RawData[5][i];
            Det4[i] =   RawData[6][i];
            EMG[i] =    RawData[7][i];
        }
        

        mlraw["time", 0] = new MLDouble("", _time, 1);
        mlraw["state1", 0] = new MLDouble("", state1, 1);
        mlraw["state2", 0] = new MLDouble("", state2, 1);
        mlraw["Det1", 0] = new MLDouble("", Det1, 1);
        mlraw["Det2", 0] = new MLDouble("", Det2, 1);
        mlraw["Det3", 0] = new MLDouble("", Det3, 1);
        mlraw["Det4", 0] = new MLDouble("", Det4, 1);
        mlraw["EMG", 0] = new MLDouble("",  EMG, 1);


        MLStructure mldata = new MLStructure("Data", new int[] { 1, 1 });
        double[] _Det1a = new double[Data[2].Count];
        double[] _Det2a = new double[Data[3].Count];
        double[] _Det3a = new double[Data[4].Count];
        double[] _Det4a = new double[Data[5].Count];
        double[] _Det1b = new double[Data[6].Count];
        double[] _Det2b = new double[Data[7].Count];
        double[] _Det3b = new double[Data[8].Count];
        double[] _Det4b = new double[Data[9].Count];
        double[] _EMG = new double[Data[1].Count];

        double[] _HbO = new double[Data[12].Count];
        double[] _HbR = new double[Data[13].Count];
        double[] _HbT = new double[Data[14].Count];
        double[] _SO2 = new double[Data[15].Count];
        double[] _dODa = new double[Data[10].Count];
        double[] _dODb = new double[Data[11].Count];


        for (int i = 0; i < RawData[0].Count; i++)
        {
            _EMG[i] = Data[1][i];
            _Det1a[i] = Data[2][i];
            _Det2a[i] = Data[3][i];
            _Det3a[i] = Data[4][i];
            _Det4a[i] = Data[5][i];
            _Det1b[i] = Data[6][i];
            _Det2b[i] = Data[7][i];
            _Det3b[i] = Data[8][i];
            _Det4b[i] = Data[9][i];

            _dODa[i] = Data[10][i];
            _dODb[i] = Data[11][i];
            _HbO[i] = Data[12][i];
            _HbR[i] = Data[13][i];
            _HbT[i] = Data[14][i];
            _SO2[i] = Data[15][i];
        }


        mldata["time", 0] = new MLDouble("", _time, 1);
        mldata["Det1_660nm", 0] = new MLDouble("", _Det1a, 1);
        mldata["Det2_660nm", 0] = new MLDouble("", _Det2a, 1);
        mldata["Det3_660nm", 0] = new MLDouble("", _Det3a, 1);
        mldata["Det4_660nm", 0] = new MLDouble("", _Det4a, 1);
        mldata["Det1_850nm", 0] = new MLDouble("", _Det1b, 1);
        mldata["Det2_850nm", 0] = new MLDouble("", _Det2b, 1);
        mldata["Det3_850nm", 0] = new MLDouble("", _Det3b, 1);
        mldata["Det4_850nm", 0] = new MLDouble("", _Det4b, 1);

        mldata["dOD_660nm", 0] = new MLDouble("", _dODa, 1);
        mldata["dOD_850nm", 0] = new MLDouble("", _dODb, 1);

        mldata["EMG", 0] = new MLDouble("", _EMG, 1);

        List<MLArray> mlList = new List<MLArray>();

        mlList.Add(new MLDouble("time", _time, 1));
        mlList.Add(new MLDouble("HbO", _HbO, 1));
        mlList.Add(new MLDouble("HbR", _HbR, 1));
        mlList.Add(new MLDouble("HbT", _HbT, 1));
        mlList.Add(new MLDouble("SO2", _SO2, 1));
        mlList.Add(new MLDouble("EMG", _EMG, 1));

        mlList.Add(mldata);
        mlList.Add(mlhdr);
        mlList.Add(mlraw);

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

        double E11, E12, E21, E22;
        E11 = 73.5911;
        E12 = 742.9477;
        E21 = 243.6151;
        E22 = 159.1833;


        int cnt = 0;
        while (mainthread.IsAlive)
        {

            byte[] msg = udpClient.Receive(ref iPEndPoint);
            string msgs = Encoding.ASCII.GetString(msg);
            string[] msgss = msgs.Split(new char[] { ' ' });
            double s1 = Convert.ToDouble(msgss[0]);
            state1.Add(s1);
            double s2 = Convert.ToDouble(msgss[1]);
            state2.Add(s2);
            double tt = Convert.ToDouble(msgss[2]) / 1000;
            time.Add(tt);

            RawData[0].Add(Convert.ToDouble(msgss[0]));
            RawData[1].Add(Convert.ToDouble(msgss[1]));
            RawData[2].Add(Convert.ToDouble(msgss[2]));
            RawData[3].Add(Convert.ToDouble(msgss[3]));
            RawData[4].Add(Convert.ToDouble(msgss[4]));
            RawData[5].Add(Convert.ToDouble(msgss[5]));
            RawData[6].Add(Convert.ToDouble(msgss[6]));
            RawData[7].Add(Convert.ToDouble(msgss[7]));



            Data[0].Add(Convert.ToDouble(msgss[3]));
            Data[1].Add( EMGfilter.ProcessSample(NotchFilters[0].ProcessSample( Convert.ToDouble(msgss[3]))));


            double y1, y2, y3, y4;
            y1=  NotchFilters[1].ProcessSample(Convert.ToDouble(msgss[4]));
            y2 = NotchFilters[2].ProcessSample(Convert.ToDouble(msgss[5]));
            y3 = NotchFilters[3].ProcessSample(Convert.ToDouble(msgss[6]));
            y4 = NotchFilters[4].ProcessSample(Convert.ToDouble(msgss[7]));


            Data[2].Add(Calibration660[0]*  LowPassFilters[0].ProcessSample(s1 * y1));
            Data[3].Add(Calibration660[1] * LowPassFilters[1].ProcessSample(s1 * y2));
            Data[4].Add(Calibration660[2] * LowPassFilters[2].ProcessSample(s1 * y3));
            Data[5].Add(Calibration660[3] * LowPassFilters[3].ProcessSample(s1 * y4));
            Data[6].Add(Calibration850[0] * LowPassFilters[4].ProcessSample(s2 * y1));
            Data[7].Add(Calibration850[1] * LowPassFilters[5].ProcessSample(s2 * y2));
            Data[8].Add(Calibration850[2] * LowPassFilters[6].ProcessSample(s2 * y3));
            Data[9].Add(Calibration850[3] * LowPassFilters[7].ProcessSample(s2 * y4));

            int n = Data[9].Count - 1;

            double[] Y1 = new double[3];
            double Y0 = -Math.Log(distances[0] * Math.Max(Data[2][n],1))/distances[0];
            Y1[0] = (-Math.Log(distances[1] * Math.Max(Data[3][n], 1))- Y0)/ (distances[1]- distances[0]);
            Y1[1] = (-Math.Log(distances[2] * Math.Max(Data[4][n], 1))- Y0)/ (distances[2] - distances[0]);
            Y1[2] = (-Math.Log(distances[3] * Math.Max(Data[5][n], 1)) -Y0)/ (distances[3] - distances[0]);

            double S1 = (Y1[0] + Y1[1] + Y1[2]) / 3;
            double musp1 = 1.5473;
            double mua1 = (-3 * musp1 + Math.Sqrt(9 * musp1 * musp1 + 12 * (S1 * S1))) / 6;

            double[] Y2 = new double[3];
            Y0 = -Math.Log(distances[0] * Math.Max(Data[6][n], 1)) / distances[0];
            Y2[0] = (-Math.Log(distances[1] * Math.Max(Data[7][n], 1)) - Y0) / (distances[1] - distances[0]);
            Y2[1] = (-Math.Log(distances[2] * Math.Max(Data[8][n], 1)) - Y0) / (distances[2] - distances[0]);
            Y2[2] = (-Math.Log(distances[3] * Math.Max(Data[9][n], 1)) - Y0) / (distances[3] - distances[0]);

            double S2 = (Y2[0] + Y2[1] + Y2[2]) / 3;
            double musp2 = 1.0293;
            double mua2 = (-3 * musp2 + Math.Sqrt(9 * musp2 * musp2 + 12 * (S2 * S2))) / 6;

            mua1 = LowPassFilters2[0].ProcessSample(mua1);
            mua2 = LowPassFilters2[1].ProcessSample(mua2);


            Data[10].Add(mua1);

            //11- dOD850
            Data[11].Add(mua2);

            double det = E11 * E22 - E12 * E21;
            double HbO = 100000 * (E22 * mua1 - E12 * mua2) / det;
            double HbR = 100000 * (-E21 * mua1 + E11 * mua2) / det;
            Data[12].Add(HbO);
            Data[13].Add(HbR);
            Data[14].Add(HbO + HbR);
            Data[15].Add(HbO / (HbR + HbO));
            


            //12- HbO2
            //13- HbR
            //14- StO2
            //15- HbT


            cnt++;

            if (cnt > 500)
            {
                drawingarea1.QueueDraw();
                progressbar1.Pulse();
                cnt = 0;
            }
        }
    }

    protected void DataDraw(object sender,EventArgs e)
    {

        if (Data[0].Count <2)
        {
            return;
        }

        Gdk.Drawable da = drawingarea1.GdkWindow;

        double tMin = 0;
        if (checkbuttonWIndowTime.Active)
        {
            tMin = time[time.Count-1]-Convert.ToDouble(entrywindowTime.Text);
        }
       

        int selected = combobox5.Active;
        int datasel = 1;
        switch (selected)
        {
            case 0:  // EMG-filter
                datasel = 1;
                break;
            case 1:  //HbO
                datasel = 12;
                break;
            case 2: //HbR
                datasel = 13;
                break;
            case 3: //HbT
                datasel = 15;
                break;
            case 4: //SO2
                datasel = 14;
                break;
            case 5: // dOD 660
                datasel = 10;
                break;
            case 6: //dOD 850
                datasel = 11;
                break;
            case 7:  // EMG raw
                datasel = 0;
                break;
            case 8:  // NIRS raw
                datasel = 2;
                break;
            case 9:  // NIRS raw
                datasel = 3;
                break;
            case 10:  // NIRS raw
                datasel = 4;
                break;
            case 11:  // NIRS raw
                datasel = 5;
                break;
            case 12:  // NIRS raw
                datasel = 6;
                break;
            case 13:  // NIRS raw
                datasel = 7;
                break;
            case 14:  // NIRS raw
                datasel = 8;
                break;
            case 15:  // NIRS raw
                datasel = 9;
                break;

        }
       


        int width, height;
        da.GetSize(out width, out height);

        int StartIdx = 0;


        for(int sIdx=0; sIdx<time.Count; sIdx++)
        {
            if (time[sIdx] <= tMin)
            {
                StartIdx = sIdx;
            }
        }

        double maxY, minY;
        minY = 99999; maxY = -99999;

        for(int tIdx= StartIdx; tIdx<Data[datasel].Count; tIdx++)
        {
            double d = Data[datasel][tIdx];
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
        double rangeX = time[(int)time.Count - 1] - time[StartIdx];
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
        gc.RgbFgColor = new Gdk.Color(255,255,255);
        rarea.X = xoffset;
        rarea.Y = yoffset;
        rarea.Height = height;
        rarea.Width = width;
        da.DrawRectangle(gc, true, rarea);

        gc.RgbFgColor = new Gdk.Color(255, 0, 0);
        gc.SetLineAttributes(1, Gdk.LineStyle.Solid, Gdk.CapStyle.Projecting, Gdk.JoinStyle.Round);
        for(int tIdx=StartIdx+1; tIdx<Data[datasel].Count; tIdx++)
        {
            double y2 = (Data[datasel][tIdx] - minY) / rangeY * height;
            double y1 = (Data[datasel][tIdx-1] - minY) / rangeY * height;

            double x2 = (time[tIdx] - time[StartIdx]) / rangeX * width;
            double x1 = (time[tIdx-1] - time[StartIdx]) / rangeX * width;

            da.DrawLine(gc, (int)x1 + xoffset, (int)(height - y1 + yoffset), (int)x2 + xoffset, (int)(height - y2 + yoffset));

        }

        gc.RgbFgColor = new Gdk.Color(0,255, 0);
        for (int i=0; i<events.Count; i++)
        {
            if (events[i] > time[StartIdx])
            {
                double y2 = -1;
                double y1 = height+1;

                double x2 = (events[i] - time[StartIdx]) / rangeX * width;
                double x1 = (events[i] - time[StartIdx]) / rangeX * width;

                da.DrawLine(gc, (int)x1 + xoffset, (int)(height - y1 + yoffset), (int)x2 + xoffset, (int)(height - y2 + yoffset));
            }
        }


        gc.RgbFgColor = new Gdk.Color(0, 0, 0);
        int numxlabels = 10;
        int numylabels = 5;
        double tstart, tend, dt;
        tstart = time[StartIdx];
        tend = time[time.Count - 1];
        dt = Math.Round((tend - tstart) / (1 + numxlabels));
        if (dt < 1)
        {
            dt = 1;
        }
        for(double i=0; i<rangeX; i += dt)
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

    protected void ChangeDataView(object sender, EventArgs e)
    {
    }

    protected void MarkStimulus(object sender, EventArgs e)
    {

        if (isrunning)
        {
            events.Add(time[time.Count - 1]);

            labelEvents.Text = String.Format("# events {0}", events.Count);
        }


    }
}
