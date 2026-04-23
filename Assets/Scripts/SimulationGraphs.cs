using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class EpidemicLineGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<EpidemicLineGraph, UxmlTraits> { }
    
    public List<Vector3> sData = new List<Vector3>(), eData = new List<Vector3>(), iData = new List<Vector3>();
    public List<Vector3> rData = new List<Vector3>(), vData = new List<Vector3>(), dData = new List<Vector3>();
    
    private List<Vector3> ghostSData = new List<Vector3>(), ghostEData = new List<Vector3>(), ghostIData = new List<Vector3>();
    private List<Vector3> ghostRData = new List<Vector3>(), ghostVData = new List<Vector3>(), ghostDData = new List<Vector3>();

    public int maxPopulation = 10000;
    private int displayMaxDays = 30; 

    private Label yMaxLabel, yMidLabel, xMaxLabel, xMidLabel;
    private VisualElement graphArea;

    public System.Action<int> OnDayClicked; 
    public System.Action OnCancelClicked; 

    public int maxScrubableDay = 0; 
    private int hoveredDay = -1; 
    public int pinnedDay = -1; 
    private bool isHovering = false;

    public EpidemicLineGraph()
    {
        style.flexGrow = 1; style.minHeight = 180; style.marginTop = 10; style.marginBottom = 10;
        style.flexDirection = FlexDirection.Column; 

        var title = new Label("TOTAL INFECTION CURVE") { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } };
        Add(title);

        var mainRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel);
        mainRow.Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        
        graphArea = new VisualElement(); graphArea.AddToClassList("graph-container");
        graphArea.style.flexGrow = 1; graphArea.pickingMode = PickingMode.Position;
        graphArea.generateVisualContent += OnGenerateVisualContent;
        
        graphArea.RegisterCallback<PointerEnterEvent>(evt => isHovering = true);
        graphArea.RegisterCallback<PointerLeaveEvent>(evt => { isHovering = false; hoveredDay = -1; graphArea.MarkDirtyRepaint(); });
        graphArea.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        graphArea.RegisterCallback<PointerDownEvent>(OnPointerDown);
        
        var xAxisContainer = new VisualElement(); xAxisContainer.AddToClassList("x-axis-container");
        var xMinLabel = new Label("Day 0"); xMinLabel.AddToClassList("axis-label");
        xMidLabel = new Label("Day 15");    xMidLabel.AddToClassList("axis-label");
        xMaxLabel = new Label("Day 30");    xMaxLabel.AddToClassList("axis-label");
        xAxisContainer.Add(xMinLabel); xAxisContainer.Add(xMidLabel); xAxisContainer.Add(xMaxLabel);

        rightColumn.Add(graphArea); rightColumn.Add(xAxisContainer);
        mainRow.Add(rightColumn);
        
        Add(mainRow);
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (!isHovering) return;
        float width = graphArea.contentRect.width;
        float normalizedX = Mathf.Clamp01(evt.localPosition.x / width);
        hoveredDay = Mathf.RoundToInt(normalizedX * displayMaxDays);
        graphArea.MarkDirtyRepaint(); 
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.button == 0) { if (hoveredDay >= 0 && hoveredDay <= maxScrubableDay) { pinnedDay = hoveredDay; OnDayClicked?.Invoke(pinnedDay); graphArea.MarkDirtyRepaint(); } }
        else if (evt.button == 1) { if (pinnedDay != -1) { pinnedDay = -1; OnCancelClicked?.Invoke(); graphArea.MarkDirtyRepaint(); } }
    }

    public void SetGhostBaseline() { ghostSData = new List<Vector3>(sData); ghostEData = new List<Vector3>(eData); ghostIData = new List<Vector3>(iData); ghostRData = new List<Vector3>(rData); ghostVData = new List<Vector3>(vData); ghostDData = new List<Vector3>(dData); graphArea.MarkDirtyRepaint(); }
    public void RestoreFromGhost() { sData = new List<Vector3>(ghostSData); eData = new List<Vector3>(ghostEData); iData = new List<Vector3>(ghostIData); rData = new List<Vector3>(ghostRData); vData = new List<Vector3>(ghostVData); dData = new List<Vector3>(ghostDData); displayMaxDays = sData.Count > 0 ? Mathf.Max(Mathf.CeilToInt(sData[sData.Count - 1].x), 30) : 30; maxScrubableDay = displayMaxDays; graphArea.MarkDirtyRepaint(); }
    public void ClearData() { sData.Clear(); eData.Clear(); iData.Clear(); rData.Clear(); vData.Clear(); dData.Clear(); ghostSData.Clear(); ghostEData.Clear(); ghostIData.Clear(); ghostRData.Clear(); ghostVData.Clear(); ghostDData.Clear(); displayMaxDays = 30; hoveredDay = -1; pinnedDay = -1; maxScrubableDay = 0; graphArea.MarkDirtyRepaint(); }
    public void ClearGhostBaseline() { ghostSData.Clear(); ghostEData.Clear(); ghostIData.Clear(); ghostRData.Clear(); ghostVData.Clear(); ghostDData.Clear(); graphArea.MarkDirtyRepaint(); }

    public void TruncateData(int branchDay)
    {
        float exactBranchTime = branchDay - 1f; 
        sData.RemoveAll(v => v.x > exactBranchTime); eData.RemoveAll(v => v.x > exactBranchTime); iData.RemoveAll(v => v.x > exactBranchTime); rData.RemoveAll(v => v.x > exactBranchTime); vData.RemoveAll(v => v.x > exactBranchTime); dData.RemoveAll(v => v.x > exactBranchTime);
        displayMaxDays = Mathf.Max(branchDay, 30); int maxGhostDay = ghostSData.Count > 0 ? Mathf.CeilToInt(ghostSData[ghostSData.Count - 1].x) : 0; displayMaxDays = Mathf.Max(displayMaxDays, maxGhostDay);
        maxScrubableDay = branchDay; pinnedDay = -1; graphArea.MarkDirtyRepaint();
    }

    public void AddData(int s, int e, int i, int r, int v, int d, float day)
    {
        displayMaxDays = Mathf.Max(Mathf.CeilToInt(day), 30); 
        int maxGhostDay = ghostSData.Count > 0 ? Mathf.CeilToInt(ghostSData[ghostSData.Count - 1].x) : 0; displayMaxDays = Mathf.Max(displayMaxDays, maxGhostDay);
        yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k"; yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k"; xMaxLabel.text = "Day " + displayMaxDays; xMidLabel.text = "Day " + Mathf.RoundToInt(displayMaxDays / 2f);
        sData.Add(new Vector3(day, s, 0)); eData.Add(new Vector3(day, e, 0)); iData.Add(new Vector3(day, i, 0)); rData.Add(new Vector3(day, r, 0)); vData.Add(new Vector3(day, v, 0)); dData.Add(new Vector3(day, d, 0)); graphArea.MarkDirtyRepaint();
    }

    void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        float w = graphArea.contentRect.width; float h = graphArea.contentRect.height; DrawGridAndAxes(ctx, w, h);
        DrawLine(ctx, ghostSData, new Color(0.2f, 0.8f, 0.8f, 0.3f), w, h); DrawLine(ctx, ghostEData, new Color(1f, 0.6f, 0f, 0.3f), w, h); DrawLine(ctx, ghostIData, new Color(1f, 0.2f, 0.2f, 0.3f), w, h); DrawLine(ctx, ghostRData, new Color(0.2f, 0.8f, 0.2f, 0.3f), w, h); DrawLine(ctx, ghostVData, new Color(1f, 0.9f, 0.2f, 0.3f), w, h); DrawLine(ctx, ghostDData, new Color(0.4f, 0.4f, 0.4f, 0.3f), w, h); 
        DrawLine(ctx, sData, new Color(0.2f, 0.8f, 0.8f, 1f), w, h); DrawLine(ctx, eData, new Color(1f, 0.6f, 0f, 1f), w, h); DrawLine(ctx, iData, new Color(1f, 0.2f, 0.2f, 1f), w, h); DrawLine(ctx, rData, new Color(0.2f, 0.8f, 0.2f, 1f), w, h); DrawLine(ctx, vData, new Color(1f, 0.9f, 0.2f, 1f), w, h); DrawLine(ctx, dData, new Color(0.4f, 0.4f, 0.4f, 1f), w, h); 

        if (pinnedDay >= 0 && pinnedDay <= maxScrubableDay) {
            var p2D = ctx.painter2D; float lineX = ((float)pinnedDay / displayMaxDays) * w;
            p2D.strokeColor = new Color(1f, 0.8f, 0.2f, 1f); p2D.lineWidth = 2f; p2D.BeginPath(); p2D.MoveTo(new Vector2(lineX, 0)); p2D.LineTo(new Vector2(lineX, h)); p2D.Stroke();
            p2D.fillColor = new Color(1f, 0.8f, 0.2f, 1f); p2D.BeginPath(); p2D.MoveTo(new Vector2(lineX - 6, 0)); p2D.LineTo(new Vector2(lineX + 6, 0)); p2D.LineTo(new Vector2(lineX, 8)); p2D.ClosePath(); p2D.Fill();
        }
        else if (isHovering && hoveredDay >= 0 && hoveredDay <= maxScrubableDay) {
            var p2D = ctx.painter2D; float lineX = ((float)hoveredDay / displayMaxDays) * w;
            p2D.strokeColor = new Color(1f, 1f, 1f, 0.5f); p2D.lineWidth = 1.5f; p2D.BeginPath(); p2D.MoveTo(new Vector2(lineX, 0)); p2D.LineTo(new Vector2(lineX, h)); p2D.Stroke();
        }
    }

    void DrawLine(MeshGenerationContext ctx, List<Vector3> data, Color color, float width, float height) { if (data.Count < 2) return; var paint2D = ctx.painter2D; paint2D.strokeColor = color; paint2D.lineWidth = 2f; paint2D.BeginPath(); for (int j = 0; j < data.Count; j++) { float x = (data[j].x / (float)displayMaxDays) * width; float y = height - ((data[j].y / maxPopulation) * height); if (j == 0) paint2D.MoveTo(new Vector2(x, y)); else paint2D.LineTo(new Vector2(x, y)); } paint2D.Stroke(); }
    public static void DrawGridAndAxes(MeshGenerationContext ctx, float width, float height) { var paint2D = ctx.painter2D; paint2D.strokeColor = new Color(0.3f, 0.3f, 0.3f, 0.5f); paint2D.lineWidth = 1f; paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height / 2)); paint2D.LineTo(new Vector2(width, height / 2)); paint2D.Stroke(); paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height / 4)); paint2D.LineTo(new Vector2(width, height / 4)); paint2D.Stroke(); paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height * 0.75f)); paint2D.LineTo(new Vector2(width, height * 0.75f)); paint2D.Stroke(); paint2D.BeginPath(); paint2D.MoveTo(new Vector2(width / 2, 0)); paint2D.LineTo(new Vector2(width / 2, height)); paint2D.Stroke(); paint2D.strokeColor = new Color(0.6f, 0.6f, 0.6f, 1f); paint2D.lineWidth = 2f; paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, 0)); paint2D.LineTo(new Vector2(0, height)); paint2D.MoveTo(new Vector2(0, height)); paint2D.LineTo(new Vector2(width, height)); paint2D.Stroke(); }
}

public class EpidemicBarGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<EpidemicBarGraph, UxmlTraits> { }
    
    private VisualElement sBar, eBar, iBar, rBar, vBar, dBar;
    private Label sLbl, eLbl, iLbl, rLbl, vLbl, dLbl;
    private Label yMaxLabel, yMidLabel;
    public int maxPopulation = 10000;

    public EpidemicBarGraph()
    {
        style.flexGrow = 1; style.minHeight = 160; style.flexDirection = FlexDirection.Column; style.marginTop = 15; style.marginBottom = 15;

        var title = new Label("POPULATION DISTRIBUTION") { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } };
        Add(title);

        var mainLayoutRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

        var graphSection = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel);
        graphSection.Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        var graphArea = new VisualElement(); graphArea.AddToClassList("graph-container");
        graphArea.style.flexDirection = FlexDirection.Row; 
        graphArea.generateVisualContent += (ctx) => { EpidemicLineGraph.DrawGridAndAxes(ctx, graphArea.contentRect.width, graphArea.contentRect.height); };

        sBar = CreateVerticalBar(new Color(0.2f, 0.8f, 0.8f)); eBar = CreateVerticalBar(new Color(1f, 0.6f, 0f));
        iBar = CreateVerticalBar(new Color(1f, 0.2f, 0.2f)); rBar = CreateVerticalBar(new Color(0.2f, 0.8f, 0.2f));
        vBar = CreateVerticalBar(new Color(1f, 0.9f, 0.2f)); dBar = CreateVerticalBar(new Color(0.4f, 0.4f, 0.4f));

        graphArea.Add(sBar.parent); graphArea.Add(eBar.parent); graphArea.Add(iBar.parent); graphArea.Add(rBar.parent); graphArea.Add(vBar.parent); graphArea.Add(dBar.parent);
        rightColumn.Add(graphArea);

        var labelRow = new VisualElement(); labelRow.AddToClassList("x-axis-container"); labelRow.style.marginTop = 2;
        sLbl = CreateLabel("0"); eLbl = CreateLabel("0"); iLbl = CreateLabel("0");
        rLbl = CreateLabel("0"); vLbl = CreateLabel("0"); dLbl = CreateLabel("0");
        labelRow.Add(sLbl); labelRow.Add(eLbl); labelRow.Add(iLbl); labelRow.Add(rLbl); labelRow.Add(vLbl); labelRow.Add(dLbl);
        rightColumn.Add(labelRow);

        graphSection.Add(rightColumn);
        mainLayoutRow.Add(graphSection);

        var legendCol = new VisualElement { style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center, marginLeft = 15, width = 85 } };
        legendCol.Add(CreateLegendItem("Susceptible", new Color(0.2f, 0.8f, 0.8f)));
        legendCol.Add(CreateLegendItem("Exposed", new Color(1f, 0.6f, 0f)));
        legendCol.Add(CreateLegendItem("Infected", new Color(1f, 0.2f, 0.2f)));
        legendCol.Add(CreateLegendItem("Recovered", new Color(0.2f, 0.8f, 0.2f)));
        legendCol.Add(CreateLegendItem("Vaccinated", new Color(1f, 0.9f, 0.2f)));
        legendCol.Add(CreateLegendItem("Dead", new Color(0.4f, 0.4f, 0.4f)));
        
        mainLayoutRow.Add(legendCol);
        Add(mainLayoutRow);
    }

    private VisualElement CreateLegendItem(string name, Color c) { var item = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } }; var box = new VisualElement { style = { width = 10, height = 10, backgroundColor = c, marginRight = 6 } }; var lbl = new Label(name) { style = { color = new Color(0.8f, 0.8f, 0.8f, 1f), fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold } }; item.Add(box); item.Add(lbl); return item; }
    private VisualElement CreateVerticalBar(Color c) { var wrapper = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.FlexEnd, marginLeft = 2, marginRight = 2 } }; var bar = new VisualElement { style = { height = Length.Percent(2), backgroundColor = c, borderTopLeftRadius = 2, borderTopRightRadius = 2 } }; wrapper.Add(bar); return bar; }
    private Label CreateLabel(string txt) { return new Label(txt) { style = { flexGrow = 1, color = Color.white, fontSize = 9, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleCenter } }; }

    public void ClearData() { sBar.style.height = Length.Percent(2f); sLbl.text = "0"; eBar.style.height = Length.Percent(2f); eLbl.text = "0"; iBar.style.height = Length.Percent(2f); iLbl.text = "0"; rBar.style.height = Length.Percent(2f); rLbl.text = "0"; vBar.style.height = Length.Percent(2f); vLbl.text = "0"; dBar.style.height = Length.Percent(2f); dLbl.text = "0"; }
    public void UpdateData(int s, int e, int i, int r, int v, int d) { float total = Mathf.Max(maxPopulation, 1); yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k"; yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k"; sBar.style.height = Length.Percent(Mathf.Max((s / total) * 100f, 2f)); sLbl.text = s.ToString(); eBar.style.height = Length.Percent(Mathf.Max((e / total) * 100f, 2f)); eLbl.text = e.ToString(); iBar.style.height = Length.Percent(Mathf.Max((i / total) * 100f, 2f)); iLbl.text = i.ToString(); rBar.style.height = Length.Percent(Mathf.Max((r / total) * 100f, 2f)); rLbl.text = r.ToString(); vBar.style.height = Length.Percent(Mathf.Max((v / total) * 100f, 2f)); vLbl.text = v.ToString(); dBar.style.height = Length.Percent(Mathf.Max((d / total) * 100f, 2f)); dLbl.text = d.ToString(); }
}

public class ActiveCasesGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<ActiveCasesGraph, UxmlTraits> { }
    private List<Vector2> casesData = new List<Vector2>(); public int maxPopulation = 10000; private int displayMaxDays = 30; 
    private Label yMaxLabel, yMidLabel, xMaxLabel, xMidLabel; private VisualElement graphArea;

    public ActiveCasesGraph()
    {
        style.flexGrow = 1; style.minHeight = 140; style.marginBottom = 10; style.flexDirection = FlexDirection.Column; 

        var title = new Label("ACTIVE INFECTION SPIKE") { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } };
        Add(title);

        var mainRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel); 
        mainRow.Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        graphArea = new VisualElement(); graphArea.AddToClassList("graph-container"); graphArea.generateVisualContent += OnGenerateVisualContent;
        var xAxisContainer = new VisualElement(); xAxisContainer.AddToClassList("x-axis-container");
        var xMinLabel = new Label("Day 0"); xMinLabel.AddToClassList("axis-label");
        xMidLabel = new Label("Day 15");    xMidLabel.AddToClassList("axis-label");
        xMaxLabel = new Label("Day 30");    xMaxLabel.AddToClassList("axis-label");
        xAxisContainer.Add(xMinLabel); xAxisContainer.Add(xMidLabel); xAxisContainer.Add(xMaxLabel);

        rightColumn.Add(graphArea); rightColumn.Add(xAxisContainer); 
        mainRow.Add(rightColumn);

        Add(mainRow);
    }

    public void ClearData() { casesData.Clear(); displayMaxDays = 30; graphArea.MarkDirtyRepaint(); }
    public void TruncateData(int branchDay) { float exactBranchTime = branchDay - 1f; casesData.RemoveAll(v => v.x > exactBranchTime); displayMaxDays = Mathf.Max(branchDay, 30); graphArea.MarkDirtyRepaint(); }
    public void AddData(int currentInfected, float day) { displayMaxDays = Mathf.Max(Mathf.CeilToInt(day), 30); yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k"; yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k"; xMaxLabel.text = "Day " + displayMaxDays; xMidLabel.text = "Day " + Mathf.RoundToInt(displayMaxDays / 2f); casesData.Add(new Vector2(day, currentInfected)); graphArea.MarkDirtyRepaint(); }

    void OnGenerateVisualContent(MeshGenerationContext ctx) { float w = graphArea.contentRect.width; float h = graphArea.contentRect.height; EpidemicLineGraph.DrawGridAndAxes(ctx, w, h); if (casesData.Count < 2) return; float yMax = maxPopulation; var paint2D = ctx.painter2D; paint2D.strokeColor = new Color(1f, 0.2f, 0.2f); paint2D.lineWidth = 2f; paint2D.fillColor = new Color(1f, 0.2f, 0.2f, 0.3f); paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, h)); for (int j = 0; j < casesData.Count; j++) { float x = (casesData[j].x / (float)displayMaxDays) * w; float y = h - ((casesData[j].y / yMax) * h); if (j == 0) paint2D.MoveTo(new Vector2(x, y)); else paint2D.LineTo(new Vector2(x, y)); } paint2D.LineTo(new Vector2((casesData[casesData.Count - 1].x / (float)displayMaxDays) * w, h)); paint2D.ClosePath(); paint2D.Fill(); paint2D.Stroke(); }
}

public class SimulationStatsDashboard : VisualElement
{
    public new class UxmlFactory : UxmlFactory<SimulationStatsDashboard, UxmlTraits> { }

    private Label aliveLabel, deadLabel;
    private Label susceptibleLabel, exposedLabel, infectedLabel, recoveredLabel, vaccinatedLabel;
    private Label hospitalLabel, vaccineLabel;

    public SimulationStatsDashboard()
    {
        style.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1f); 
        style.flexDirection = FlexDirection.Column; 
        style.flexGrow = 0; 
        style.flexShrink = 0; 
        style.height = new StyleLength(StyleKeyword.Auto); 
        style.paddingTop = 15; style.paddingBottom = 15; style.paddingLeft = 15; style.paddingRight = 15;
        style.borderTopWidth = 1; style.borderTopColor = new Color(0.2f, 0.2f, 0.2f, 1f); style.marginBottom = 10;

        var title = new Label("SIMULATION STATISTICS") { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 12 } };
        Add(title);

        var statsGrid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignContent = Align.FlexStart } };

        VisualElement CreateItem(string titleText, out Label valueLabel, Color valColor)
        {
            var item = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexStart, width = 170, marginRight = 10, marginBottom = 4 } };
            var itemTitle = new Label(titleText) { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, width = 85, paddingLeft = 0, marginLeft = 0 } };
            valueLabel = new Label("0") { style = { color = valColor, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 0, marginLeft = 0 } };
            item.Add(itemTitle); item.Add(valueLabel); return item;
        }

        statsGrid.Add(CreateItem("Hosp. Beds:", out hospitalLabel, new Color(1f, 0.5f, 0.5f, 1f)));
        statsGrid.Add(CreateItem("Vaccines:", out vaccineLabel, new Color(0.5f, 0.8f, 1f, 1f)));

        statsGrid.Add(CreateItem("Total Alive:", out aliveLabel, Color.white));
        statsGrid.Add(CreateItem("Total Dead:", out deadLabel, new Color(0.6f, 0.6f, 0.6f, 1f)));
        statsGrid.Add(CreateItem("Susceptible:", out susceptibleLabel, new Color(0.2f, 0.8f, 0.8f, 1f)));
        statsGrid.Add(CreateItem("Exposed:", out exposedLabel, new Color(1f, 0.6f, 0f, 1f)));

        statsGrid.Add(CreateItem("Infected:", out infectedLabel, new Color(1f, 0.2f, 0.2f, 1f)));
        statsGrid.Add(CreateItem("Recovered:", out recoveredLabel, new Color(0.2f, 0.8f, 0.2f, 1f)));
        statsGrid.Add(CreateItem("Vaccinated:", out vaccinatedLabel, new Color(1f, 0.9f, 0.2f, 1f)));

        Add(statsGrid);
    }

    public void ClearData() { aliveLabel.text = "0"; deadLabel.text = "0"; susceptibleLabel.text = "0"; exposedLabel.text = "0"; infectedLabel.text = "0"; recoveredLabel.text = "0"; vaccinatedLabel.text = "0"; hospitalLabel.text = "0 / 0"; vaccineLabel.text = "0 / 0"; }
    public void UpdateData(int alive, int dead, int s, int e, int i, int r, int v, int hospUsed, int hospTotal, int vacLeft, int vacTotal) { aliveLabel.text = alive.ToString("N0"); deadLabel.text = dead.ToString("N0"); susceptibleLabel.text = s.ToString("N0"); exposedLabel.text = e.ToString("N0"); infectedLabel.text = i.ToString("N0"); recoveredLabel.text = r.ToString("N0"); vaccinatedLabel.text = v.ToString("N0"); hospitalLabel.text = $"{hospUsed} / {hospTotal}"; vaccineLabel.text = $"{vacLeft} / {vacTotal}"; }
}

public class VirusVaccineControls : VisualElement
{
    public new class UxmlFactory : UxmlFactory<VirusVaccineControls, UxmlTraits> { }

    public System.Action<int, float, float, float, float> OnMutateVirus;
    public System.Action<int, int, float, float> OnDeployVaccine;

    private VisualElement activeStrainsContainer;
    private VisualElement activeVaccinesContainer;

    public VirusVaccineControls()
    {
        style.flexGrow = 1; 
        style.paddingTop = 10; 
        style.paddingBottom = 10; 
        style.paddingLeft = 0;   
        style.paddingRight = 0;  
        style.flexDirection = FlexDirection.Column;

        var scrollArea = new ScrollView(ScrollViewMode.Vertical);
        scrollArea.style.flexGrow = 1; 
        scrollArea.contentContainer.style.paddingRight = 0; 
        StyleCustomScrollbar(scrollArea);

        var mainTitle = new Label("INTERVENTION CONTROLS") { 
            style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8, marginTop = 8, paddingLeft = 15, unityTextAlign = TextAnchor.MiddleLeft } 
        };
        scrollArea.Add(mainTitle);

        var virusFoldout = new Foldout { text = "Virus Mutation Parameters" }; 
        StyleInspectorFoldout(virusFoldout); 
        
        var virusContent = new VisualElement { 
            style = { paddingLeft = 15, paddingRight = 15, paddingTop = 10, paddingBottom = 15, backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f), color = Color.white } 
        }; 

        var vStrainField = new IntegerField("New Strain Level:") { value = 2 }; 
        vStrainField.AddToClassList("inspector-field"); vStrainField.style.color = Color.white; 
        virusContent.Add(WrapWithInfoButton(vStrainField, "New Strain Level"));

        Slider vEvasionSlider, vTransSlider, vFatalitySlider;
        virusContent.Add(CreateSliderRow("Immunity Evasion:", 0f, 1f, 0.30f, out vEvasionSlider));
        virusContent.Add(CreateSliderRow("Infection Rate:", 0.01f, 1.0f, 0.40f, out vTransSlider));
        
        var vIncubationField = new FloatField("Incubation (Days):") { value = 5f }; 
        vIncubationField.AddToClassList("inspector-field"); vIncubationField.style.color = Color.white; 
        virusContent.Add(WrapWithInfoButton(vIncubationField, "Incubation (Days)"));
        
        virusContent.Add(CreateSliderRow("Mortality Rate:", 0f, 1.0f, 0.02f, out vFatalitySlider));

        activeStrainsContainer = CreateListBoxContainer(); UpdateActiveStrains(new Dictionary<int, int>()); virusContent.Add(activeStrainsContainer);

        var mutateBtn = new Button(() => { OnMutateVirus?.Invoke(vStrainField.value, vEvasionSlider.value, vTransSlider.value, vIncubationField.value, vFatalitySlider.value); }) { text = "TRIGGER MUTATION" };
        mutateBtn.AddToClassList("action-button"); mutateBtn.style.marginTop = 10; mutateBtn.style.marginBottom = 10; 
        AttachHoverTooltip(mutateBtn, "Trigger Mutation");
        virusContent.Add(mutateBtn);
        
        virusFoldout.Add(virusContent); 
        scrollArea.Add(virusFoldout);

        var vaccineFoldout = new Foldout { text = "Vaccine Deployment" }; 
        StyleInspectorFoldout(vaccineFoldout); 
        
        var vaccineContent = new VisualElement { 
            style = { paddingLeft = 15, paddingRight = 15, paddingTop = 10, paddingBottom = 15, backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f), color = Color.white } 
        }; 
        
        var vacStrainField = new IntegerField("Vaccine Strain:") { value = 1 }; 
        var vacDosesField = new IntegerField("Supply Doses:") { value = 5000 };
        vacStrainField.AddToClassList("inspector-field"); vacStrainField.style.color = Color.white;
        vacDosesField.AddToClassList("inspector-field"); vacDosesField.style.color = Color.white;
        
        vaccineContent.Add(WrapWithInfoButton(vacStrainField, "Vaccine Strain"));
        vaccineContent.Add(WrapWithInfoButton(vacDosesField, "Supply Doses"));

        Slider vacEfficacySlider, vacAbidanceSlider;
        vaccineContent.Add(CreateSliderRow("Base Efficacy:", 0f, 1f, 0.90f, out vacEfficacySlider));
        vaccineContent.Add(CreateSliderRow("Public Abidance:", 0f, 1f, 0.75f, out vacAbidanceSlider));

        activeVaccinesContainer = CreateListBoxContainer(); UpdateActiveVaccines(new Dictionary<int, int>()); vaccineContent.Add(activeVaccinesContainer);

        var deployBtn = new Button(() => { OnDeployVaccine?.Invoke(vacStrainField.value, vacDosesField.value, vacEfficacySlider.value, vacAbidanceSlider.value); }) { text = "DEPLOY WAVE" };
        deployBtn.AddToClassList("action-button"); deployBtn.style.marginTop = 10; deployBtn.style.marginBottom = 10; 
        AttachHoverTooltip(deployBtn, "Deploy Wave");
        vaccineContent.Add(deployBtn);
        
        vaccineFoldout.Add(vaccineContent); 
        scrollArea.Add(vaccineFoldout);
        
        virusFoldout.value = true; vaccineFoldout.value = true;
        Add(scrollArea);
    }

    private Button CreateInfoButton(string key)
    {
        var btn = new Button() { text = "?" };
        btn.style.width = 16;
        btn.style.height = 16;
        btn.style.borderTopLeftRadius = 8;
        btn.style.borderTopRightRadius = 8;
        btn.style.borderBottomLeftRadius = 8;
        btn.style.borderBottomRightRadius = 8;
        btn.style.backgroundColor = new Color(0.34f, 0.34f, 0.34f, 1f);  
        btn.style.borderTopWidth = 1;
        btn.style.borderBottomWidth = 1;
        btn.style.borderLeftWidth = 1;
        btn.style.borderRightWidth = 1;
        btn.style.borderTopColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderBottomColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderLeftColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.borderRightColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        btn.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        btn.style.fontSize = 10;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.paddingTop = 0;
        btn.style.paddingBottom = 0;
        btn.style.paddingLeft = 0;
        btn.style.paddingRight = 0;
        btn.style.marginLeft = 5;

        btn.RegisterCallback<PointerEnterEvent>(evt => {
            btn.style.backgroundColor = new Color(0.42f, 0.42f, 0.42f, 1f);
            if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key))
                SimulationManager.Instance.ShowTooltip(key, btn);
        });
        btn.RegisterCallback<PointerLeaveEvent>(evt => {
            btn.style.backgroundColor = new Color(0.34f, 0.34f, 0.34f, 1f);
            if (SimulationManager.Instance != null)
                SimulationManager.Instance.HideTooltip();
        });

        return btn;
    }

    private void InjectInfoButtonToUXML(VisualElement field, string key)
    {
        if (field == null || field.parent == null) return;
        var parent = field.parent;
        var index = parent.IndexOf(field);
        
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, width = Length.Percent(100) } };
        field.style.flexGrow = 1; 
        
        parent.RemoveAt(index);
        row.Add(field);
        if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key)) row.Add(CreateInfoButton(key));
        parent.Insert(index, row);
    }

    private VisualElement WrapWithInfoButton(VisualElement field, string key)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, width = Length.Percent(100) } };
        field.style.flexGrow = 1; 
        row.Add(field);
        if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key)) row.Add(CreateInfoButton(key));
        return row;
    }

    private void AttachHoverTooltip(VisualElement element, string key)
    {
        element.RegisterCallback<PointerEnterEvent>(evt => {
            if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key))
                SimulationManager.Instance.ShowTooltip(key, element);
        });
        element.RegisterCallback<PointerLeaveEvent>(evt => {
            if (SimulationManager.Instance != null)
                SimulationManager.Instance.HideTooltip();
        });
    }

    private void StyleInspectorFoldout(Foldout f)
    {
        f.RemoveFromClassList("custom-foldout");
        f.style.marginTop = 0; f.style.marginBottom = 0; f.style.marginLeft = 0; f.style.marginRight = 0;
        f.style.borderTopWidth = 1; f.style.borderBottomWidth = 0; f.style.borderLeftWidth = 0; f.style.borderRightWidth = 0;
        f.style.borderTopColor = new Color(0.15f, 0.15f, 0.15f, 1f); f.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f, 1f); 

        f.RegisterCallback<AttachToPanelEvent>(evt => {
            var foldoutToggle = f.Q<Toggle>(className: "unity-foldout__toggle");
            if (foldoutToggle != null) {
                foldoutToggle.style.marginLeft = 15; 
                var textLabel = foldoutToggle.Q<Label>();
                if (textLabel != null) { textLabel.style.color = Color.white; textLabel.style.unityFontStyleAndWeight = FontStyle.Bold; }
            }
            var internalContent = f.Q(className: "unity-foldout__content");
            if (internalContent != null) { internalContent.style.marginLeft = 0; internalContent.style.paddingLeft = 0; internalContent.style.paddingRight = 0; }
        });
    }

    private void StyleCustomScrollbar(ScrollView sv)
    {
        sv.RegisterCallback<GeometryChangedEvent>(evt => {
            var scroller = sv.verticalScroller; if (scroller == null) return;
            scroller.style.width = 6; scroller.style.minWidth = 6; scroller.style.maxWidth = 6; scroller.style.borderLeftWidth = 0; scroller.style.borderRightWidth = 0;
            var upBtn = scroller.Q(className: "unity-scroller__high-button"); if (upBtn != null) upBtn.style.display = DisplayStyle.None;
            var downBtn = scroller.Q(className: "unity-scroller__low-button"); if (downBtn != null) downBtn.style.display = DisplayStyle.None;
            var tracker = scroller.Q(className: "unity-base-slider__tracker"); if (tracker != null) { tracker.style.backgroundColor = Color.clear; tracker.style.borderLeftWidth = 0; tracker.style.borderRightWidth = 0; }
            var dragger = scroller.Q(className: "unity-base-slider__dragger"); if (dragger != null) { dragger.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 1f); dragger.style.borderTopLeftRadius = 3; dragger.style.borderTopRightRadius = 3; dragger.style.borderBottomLeftRadius = 3; dragger.style.borderBottomRightRadius = 3; dragger.style.width = 6; dragger.style.left = 0; dragger.style.marginLeft = 0; }
        });
    }

    private VisualElement CreateListBoxContainer() { 
        return new VisualElement { 
            style = { 
                backgroundColor = new Color(0.20f, 0.20f, 0.20f, 1f), 
                borderTopWidth = 0, borderBottomWidth = 0, borderLeftWidth = 0, borderRightWidth = 0, 
                borderTopLeftRadius = 0, borderTopRightRadius = 0, borderBottomLeftRadius = 0, borderBottomRightRadius = 0, 
                paddingTop = 8, paddingBottom = 8, paddingLeft = 15, paddingRight = 15, 
                marginTop = 10, marginBottom = 10, marginLeft = -15, marginRight = -15 
            } 
        }; 
    }
    
    private Label CreateListTitle(string text) { return new Label(text) { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 6, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.3f, 0.3f), paddingBottom = 4 } }; }

    private VisualElement CreateSliderRow(string labelText, float min, float max, float defaultVal, out Slider slider)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
        slider = new Slider(labelText, min, max) { value = defaultVal, style = { flexGrow = 1, color = Color.white } }; slider.AddToClassList("inspector-field");
        var floatField = new FloatField { value = defaultVal, style = { width = 45, marginLeft = 5, color = Color.white } }; floatField.AddToClassList("inspector-field");
        var s = slider; var f = floatField;
        s.RegisterValueChangedCallback(evt => { f.SetValueWithoutNotify((float)System.Math.Round(evt.newValue, 3)); });
        f.RegisterValueChangedCallback(evt => { float val = Mathf.Clamp(evt.newValue, min, max); if (evt.newValue != val) f.SetValueWithoutNotify(val); s.SetValueWithoutNotify(val); });
        row.Add(s); row.Add(f); 
        
        string key = labelText.Replace(":", "").Trim();
        if (SimulationManager.Instance != null && SimulationManager.Instance.HasTooltip(key)) row.Add(CreateInfoButton(key));
        
        return row;
    }

    public void UpdateActiveStrains(Dictionary<int, int> strainCounts) { activeStrainsContainer.Clear(); activeStrainsContainer.Add(CreateListTitle("Active Circulating Strains")); bool hasAny = false; foreach (var kvp in strainCounts) { if (kvp.Value > 0) { var row = new Label($"• Strain v{kvp.Key}: {kvp.Value} active cases") { style = { color = new Color(0.85f, 0.85f, 0.85f), fontSize = 10, marginBottom = 2, marginLeft = 4 } }; activeStrainsContainer.Add(row); hasAny = true; } } if (!hasAny) { activeStrainsContainer.Add(new Label("No active strains.") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, marginLeft = 4 } }); } }
    public void UpdateActiveVaccines(Dictionary<int, int> vaccineCounts) { activeVaccinesContainer.Clear(); activeVaccinesContainer.Add(CreateListTitle("Current Vaccines")); bool hasAny = false; foreach (var kvp in vaccineCounts) { if (kvp.Value > 0) { var row = new Label($"• Strain v{kvp.Key} Shield: {kvp.Value} protected") { style = { color = new Color(0.85f, 0.85f, 0.85f), fontSize = 10, marginBottom = 2, marginLeft = 4 } }; activeVaccinesContainer.Add(row); hasAny = true; } } if (!hasAny) { activeVaccinesContainer.Add(new Label("No deployed vaccines.") { style = { color = new Color(0.5f, 0.5f, 0.5f), fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, marginLeft = 4 } }); } }
}

public class LoadingOverlay : VisualElement
{
    public new class UxmlFactory : UxmlFactory<LoadingOverlay, UxmlTraits> { }

    private Label statusLabel;
    private VisualElement fillBar;

    public LoadingOverlay()
    {
        style.position = Position.Absolute;
        style.top = 0; style.bottom = 0; style.left = 0; style.right = 0;
        style.backgroundColor = new Color(0f, 0f, 0f, 0.7f); 
        style.alignItems = Align.Center;
        style.justifyContent = Justify.Center;
        style.display = DisplayStyle.None; 

        var panel = new VisualElement { 
            style = { 
                backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f),
                borderTopColor = new Color(0.3f, 0.3f, 0.3f, 1f),
                borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 1f),
                borderLeftColor = new Color(0.3f, 0.3f, 0.3f, 1f),
                borderRightColor = new Color(0.3f, 0.3f, 0.3f, 1f),
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                paddingTop = 20, paddingBottom = 20, paddingLeft = 25, paddingRight = 25,
                width = 350,
                alignItems = Align.Center
            } 
        };

        statusLabel = new Label("PROCESSING DATA...") { 
            style = { color = Color.white, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 15 } 
        };
        panel.Add(statusLabel);

        var trackBar = new VisualElement {
            style = {
                width = Length.Percent(100),
                height = 12,
                backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f),
                borderTopLeftRadius = 3, borderTopRightRadius = 3, borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
                overflow = Overflow.Hidden
            }
        };

        fillBar = new VisualElement {
            style = {
                width = Length.Percent(0),
                height = Length.Percent(100),
                backgroundColor = new Color(0.2f, 0.8f, 0.2f, 1f) 
            }
        };
        
        trackBar.Add(fillBar);
        panel.Add(trackBar);

        Add(panel);
    }

    public void Show(string message)
    {
        statusLabel.text = message;
        fillBar.style.width = Length.Percent(0);
        style.display = DisplayStyle.Flex;
    }

    public void SetProgress(float percent, string message)
    {
        statusLabel.text = message;
        fillBar.style.width = Length.Percent(Mathf.Clamp01(percent) * 100f);
    }

    public void Hide()
    {
        style.display = DisplayStyle.None;
    }
}