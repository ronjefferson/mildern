using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

// ==========================================
// WIDGET 1: Epidemic Line Graph
// ==========================================
public class EpidemicLineGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<EpidemicLineGraph, UxmlTraits> { }
    
    private List<Vector3> sData = new List<Vector3>(), eData = new List<Vector3>(), iData = new List<Vector3>();
    private List<Vector3> rData = new List<Vector3>(), vData = new List<Vector3>(), dData = new List<Vector3>();
    public int maxPopulation = 10000;
    private int displayMaxDays = 30; 

    private Label yMaxLabel, yMidLabel, xMaxLabel, xMidLabel;
    private VisualElement graphArea;

    public EpidemicLineGraph()
    {
        style.flexGrow = 1; style.minHeight = 180; style.marginTop = 10; style.marginBottom = 10;
        style.flexDirection = FlexDirection.Row; 

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel);
        Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        
        graphArea = new VisualElement(); graphArea.AddToClassList("graph-container");
        graphArea.generateVisualContent += OnGenerateVisualContent;
        
        var xAxisContainer = new VisualElement(); xAxisContainer.AddToClassList("x-axis-container");
        var xMinLabel = new Label("Day 0"); xMinLabel.AddToClassList("axis-label");
        xMidLabel = new Label("Day 15");    xMidLabel.AddToClassList("axis-label");
        xMaxLabel = new Label("Day 30");    xMaxLabel.AddToClassList("axis-label");
        xAxisContainer.Add(xMinLabel); xAxisContainer.Add(xMidLabel); xAxisContainer.Add(xMaxLabel);

        rightColumn.Add(graphArea); rightColumn.Add(xAxisContainer);
        Add(rightColumn);

        var title = new Label("POPULATION INFECTION CURVE") { style = { position = Position.Absolute, top = 5, left = 50, color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold } };
        Add(title);
    }

    public void AddData(int s, int e, int i, int r, int v, int d, float day)
    {
        displayMaxDays = Mathf.Max(Mathf.CeilToInt(day), 30); 
        yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k";
        yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k";
        xMaxLabel.text = "Day " + displayMaxDays;
        xMidLabel.text = "Day " + Mathf.RoundToInt(displayMaxDays / 2f);

        sData.Add(new Vector3(day, s, 0)); eData.Add(new Vector3(day, e, 0)); iData.Add(new Vector3(day, i, 0));
        rData.Add(new Vector3(day, r, 0)); vData.Add(new Vector3(day, v, 0)); dData.Add(new Vector3(day, d, 0));
        graphArea.MarkDirtyRepaint();
    }

    void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        float w = graphArea.contentRect.width; float h = graphArea.contentRect.height;
        DrawGridAndAxes(ctx, w, h);
        DrawLine(ctx, sData, new Color(0.2f, 0.8f, 0.8f), w, h); 
        DrawLine(ctx, eData, new Color(1f, 0.6f, 0f), w, h);     
        DrawLine(ctx, iData, new Color(1f, 0.2f, 0.2f), w, h);   
        DrawLine(ctx, rData, new Color(0.2f, 0.8f, 0.2f), w, h); 
        DrawLine(ctx, vData, new Color(0.2f, 0.4f, 1f), w, h);   
        DrawLine(ctx, dData, new Color(0.4f, 0.4f, 0.4f), w, h); 
    }

    void DrawLine(MeshGenerationContext ctx, List<Vector3> data, Color color, float width, float height)
    {
        if (data.Count < 2) return;
        var paint2D = ctx.painter2D;
        paint2D.strokeColor = color; paint2D.lineWidth = 2f;
        paint2D.BeginPath();
        
        for (int j = 0; j < data.Count; j++) {
            float x = (data[j].x / (float)displayMaxDays) * width;
            float y = height - ((data[j].y / maxPopulation) * height);
            if (j == 0) paint2D.MoveTo(new Vector2(x, y)); else paint2D.LineTo(new Vector2(x, y));
        }
        paint2D.Stroke();
    }

    public static void DrawGridAndAxes(MeshGenerationContext ctx, float width, float height)
    {
        var paint2D = ctx.painter2D;
        paint2D.strokeColor = new Color(0.3f, 0.3f, 0.3f, 0.5f); paint2D.lineWidth = 1f;
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height / 2)); paint2D.LineTo(new Vector2(width, height / 2)); paint2D.Stroke();
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height / 4)); paint2D.LineTo(new Vector2(width, height / 4)); paint2D.Stroke();
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, height * 0.75f)); paint2D.LineTo(new Vector2(width, height * 0.75f)); paint2D.Stroke();
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(width / 2, 0)); paint2D.LineTo(new Vector2(width / 2, height)); paint2D.Stroke();
        
        paint2D.strokeColor = new Color(0.6f, 0.6f, 0.6f, 1f); paint2D.lineWidth = 2f;
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, 0)); paint2D.LineTo(new Vector2(0, height)); 
        paint2D.MoveTo(new Vector2(0, height)); paint2D.LineTo(new Vector2(width, height)); paint2D.Stroke();
    }
}

// ==========================================
// WIDGET 2: Bar Graph
// ==========================================
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

        var legendRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center, marginBottom = 5, flexWrap = Wrap.Wrap } };
        legendRow.Add(CreateLegendItem("Susceptible", new Color(0.2f, 0.8f, 0.8f)));
        legendRow.Add(CreateLegendItem("Exposed", new Color(1f, 0.6f, 0f)));
        legendRow.Add(CreateLegendItem("Infected", new Color(1f, 0.2f, 0.2f)));
        legendRow.Add(CreateLegendItem("Recovered", new Color(0.2f, 0.8f, 0.2f)));
        legendRow.Add(CreateLegendItem("Vaccinated", new Color(0.2f, 0.4f, 1f)));
        legendRow.Add(CreateLegendItem("Dead", new Color(0.4f, 0.4f, 0.4f)));
        Add(legendRow);

        var mainGraphRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel);
        mainGraphRow.Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        
        var graphArea = new VisualElement(); graphArea.AddToClassList("graph-container");
        graphArea.style.flexDirection = FlexDirection.Row; 
        graphArea.generateVisualContent += (ctx) => { EpidemicLineGraph.DrawGridAndAxes(ctx, graphArea.contentRect.width, graphArea.contentRect.height); };

        sBar = CreateVerticalBar(new Color(0.2f, 0.8f, 0.8f)); eBar = CreateVerticalBar(new Color(1f, 0.6f, 0f));
        iBar = CreateVerticalBar(new Color(1f, 0.2f, 0.2f)); rBar = CreateVerticalBar(new Color(0.2f, 0.8f, 0.2f));
        vBar = CreateVerticalBar(new Color(0.2f, 0.4f, 1f)); dBar = CreateVerticalBar(new Color(0.4f, 0.4f, 0.4f));

        graphArea.Add(sBar.parent); graphArea.Add(eBar.parent); graphArea.Add(iBar.parent); graphArea.Add(rBar.parent); graphArea.Add(vBar.parent); graphArea.Add(dBar.parent);
        rightColumn.Add(graphArea);

        var labelRow = new VisualElement(); labelRow.AddToClassList("x-axis-container"); labelRow.style.marginTop = 2;
        sLbl = CreateLabel("0"); eLbl = CreateLabel("0"); iLbl = CreateLabel("0");
        rLbl = CreateLabel("0"); vLbl = CreateLabel("0"); dLbl = CreateLabel("0");
        
        labelRow.Add(sLbl); labelRow.Add(eLbl); labelRow.Add(iLbl); labelRow.Add(rLbl); labelRow.Add(vLbl); labelRow.Add(dLbl);
        rightColumn.Add(labelRow);

        mainGraphRow.Add(rightColumn);
        Add(mainGraphRow);
    }

    private VisualElement CreateLegendItem(string name, Color c)
    {
        var item = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginRight = 8, marginBottom = 2 } };
        var box = new VisualElement { style = { width = 10, height = 10, backgroundColor = c, marginRight = 4 } };
        var lbl = new Label(name) { style = { color = new Color(0.8f, 0.8f, 0.8f, 1f), fontSize = 10, unityFontStyleAndWeight = FontStyle.Bold } };
        item.Add(box); item.Add(lbl);
        return item;
    }

    private VisualElement CreateVerticalBar(Color c)
    {
        var wrapper = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.FlexEnd, marginLeft = 2, marginRight = 2 } };
        var bar = new VisualElement { style = { height = Length.Percent(2), backgroundColor = c, borderTopLeftRadius = 2, borderTopRightRadius = 2 } };
        wrapper.Add(bar);
        return bar; 
    }

    private Label CreateLabel(string txt) { return new Label(txt) { style = { flexGrow = 1, color = Color.white, fontSize = 9, unityFontStyleAndWeight = FontStyle.Bold, unityTextAlign = TextAnchor.MiddleCenter } }; }

    public void UpdateData(int s, int e, int i, int r, int v, int d)
    {
        float total = Mathf.Max(maxPopulation, 1);
        yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k";
        yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k";

        sBar.style.height = Length.Percent(Mathf.Max((s / total) * 100f, 2f)); sLbl.text = s.ToString();
        eBar.style.height = Length.Percent(Mathf.Max((e / total) * 100f, 2f)); eLbl.text = e.ToString();
        iBar.style.height = Length.Percent(Mathf.Max((i / total) * 100f, 2f)); iLbl.text = i.ToString();
        rBar.style.height = Length.Percent(Mathf.Max((r / total) * 100f, 2f)); rLbl.text = r.ToString();
        vBar.style.height = Length.Percent(Mathf.Max((v / total) * 100f, 2f)); vLbl.text = v.ToString();
        dBar.style.height = Length.Percent(Mathf.Max((d / total) * 100f, 2f)); dLbl.text = d.ToString();
    }
}

// ==========================================
// WIDGET 3: Active Cases Graph
// ==========================================
public class ActiveCasesGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<ActiveCasesGraph, UxmlTraits> { }
    
    private List<Vector2> casesData = new List<Vector2>();
    public int maxPopulation = 10000; 
    private int displayMaxDays = 30; 

    private Label yMaxLabel, yMidLabel, xMaxLabel, xMidLabel;
    private VisualElement graphArea;

    public ActiveCasesGraph()
    {
        style.flexGrow = 1; style.minHeight = 140; style.marginBottom = 10;
        style.flexDirection = FlexDirection.Row; 

        var yAxisContainer = new VisualElement(); yAxisContainer.AddToClassList("y-axis-container");
        yMaxLabel = new Label("10k"); yMaxLabel.AddToClassList("axis-label"); yMaxLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yMidLabel = new Label("5k");  yMidLabel.AddToClassList("axis-label"); yMidLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        var yMinLabel = new Label("0"); yMinLabel.AddToClassList("axis-label"); yMinLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        yAxisContainer.Add(yMaxLabel); yAxisContainer.Add(yMidLabel); yAxisContainer.Add(yMinLabel);
        Add(yAxisContainer);

        var rightColumn = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
        
        graphArea = new VisualElement(); graphArea.AddToClassList("graph-container");
        graphArea.generateVisualContent += OnGenerateVisualContent;
        
        var xAxisContainer = new VisualElement(); xAxisContainer.AddToClassList("x-axis-container");
        var xMinLabel = new Label("Day 0"); xMinLabel.AddToClassList("axis-label");
        xMidLabel = new Label("Day 15");    xMidLabel.AddToClassList("axis-label");
        xMaxLabel = new Label("Day 30");    xMaxLabel.AddToClassList("axis-label");
        xAxisContainer.Add(xMinLabel); xAxisContainer.Add(xMidLabel); xAxisContainer.Add(xMaxLabel);

        rightColumn.Add(graphArea); rightColumn.Add(xAxisContainer);
        Add(rightColumn);

        var title = new Label("ACTIVE INFECTION SPIKE") { style = { position = Position.Absolute, top = 5, left = 50, color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold } };
        Add(title);
    }

    public void AddData(int currentInfected, float day)
    {
        displayMaxDays = Mathf.Max(Mathf.CeilToInt(day), 30);
        yMaxLabel.text = (maxPopulation / 1000f).ToString("0.#") + "k";
        yMidLabel.text = ((maxPopulation / 2f) / 1000f).ToString("0.#") + "k";
        xMaxLabel.text = "Day " + displayMaxDays;
        xMidLabel.text = "Day " + Mathf.RoundToInt(displayMaxDays / 2f);

        casesData.Add(new Vector2(day, currentInfected));
        graphArea.MarkDirtyRepaint();
    }

    void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        float w = graphArea.contentRect.width; float h = graphArea.contentRect.height;
        EpidemicLineGraph.DrawGridAndAxes(ctx, w, h);

        if (casesData.Count < 2) return;
        float yMax = maxPopulation; 

        var paint2D = ctx.painter2D;
        paint2D.strokeColor = new Color(1f, 0.2f, 0.2f); paint2D.lineWidth = 2f;
        paint2D.fillColor = new Color(1f, 0.2f, 0.2f, 0.3f);
        
        paint2D.BeginPath(); paint2D.MoveTo(new Vector2(0, h));
        
        for (int j = 0; j < casesData.Count; j++) {
            float x = (casesData[j].x / (float)displayMaxDays) * w;
            float y = h - ((casesData[j].y / yMax) * h);
            if (j == 0) paint2D.MoveTo(new Vector2(x, y)); else paint2D.LineTo(new Vector2(x, y));
        }

        paint2D.LineTo(new Vector2((casesData[casesData.Count - 1].x / (float)displayMaxDays) * w, h));
        paint2D.ClosePath(); paint2D.Fill(); paint2D.Stroke();
    }
}

// ==========================================
// WIDGET 4: Simulation Stats Dashboard (REVERTED & FIXED!)
// ==========================================
public class SimulationStatsDashboard : VisualElement
{
    public new class UxmlFactory : UxmlFactory<SimulationStatsDashboard, UxmlTraits> { }

    private Label timeLabel, speedLabel;
    private Label aliveLabel, deadLabel;
    private Label susceptibleLabel, exposedLabel, infectedLabel, recoveredLabel, vaccinatedLabel;
    private Label hospitalLabel, vaccineLabel;

    public SimulationStatsDashboard()
    {
        style.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1f); 
        
        style.flexDirection = FlexDirection.Column; 
        style.flexWrap = Wrap.Wrap;
        style.alignContent = Align.FlexStart; 
        
        style.flexGrow = 1; 
        style.height = new StyleLength(StyleKeyword.Auto); 
        
        // Removed the left padding so it sits flush with everything else
        style.paddingTop = 10; style.paddingBottom = 10; style.paddingLeft = 0; style.paddingRight = 10;
        style.borderTopWidth = 1; style.borderTopColor = new Color(0.2f, 0.2f, 0.2f, 1f); 
        style.marginBottom = 10;

        VisualElement CreateItem(string titleText, out Label valueLabel, Color valColor)
        {
            // Changed to FlexStart to fix the gap, and added fixed widths
            var item = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexStart, width = 170, marginRight = 10, marginBottom = 4 } };
            
            // Forced white text, perfectly left-aligned
            var title = new Label(titleText) { style = { color = Color.white, fontSize = 11, unityFontStyleAndWeight = FontStyle.Bold, width = 85, paddingLeft = 0, marginLeft = 0 } };
            
            // Restored your custom colors!
            valueLabel = new Label("0") { style = { color = valColor, fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 0, marginLeft = 0 } };
            
            item.Add(title); item.Add(valueLabel);
            return item;
        }

        // Colors are back!
        Add(CreateItem("Day / Time:", out timeLabel, Color.white));
        Add(CreateItem("Sim Speed:", out speedLabel, new Color(0.8f, 0.8f, 0.8f, 1f)));
        Add(CreateItem("Hosp. Beds:", out hospitalLabel, new Color(1f, 0.5f, 0.5f, 1f)));
        Add(CreateItem("Vaccines:", out vaccineLabel, new Color(0.5f, 0.8f, 1f, 1f)));

        Add(CreateItem("Total Alive:", out aliveLabel, Color.white));
        Add(CreateItem("Total Dead:", out deadLabel, new Color(0.6f, 0.6f, 0.6f, 1f)));
        Add(CreateItem("Susceptible:", out susceptibleLabel, new Color(0.2f, 0.8f, 0.8f, 1f)));
        Add(CreateItem("Exposed:", out exposedLabel, new Color(1f, 0.6f, 0f, 1f)));

        Add(CreateItem("Infected:", out infectedLabel, new Color(1f, 0.2f, 0.2f, 1f)));
        Add(CreateItem("Recovered:", out recoveredLabel, new Color(0.2f, 0.8f, 0.2f, 1f)));
        Add(CreateItem("Vaccinated:", out vaccinatedLabel, new Color(0.2f, 0.4f, 1f, 1f)));
    }

    public void UpdateData(int day, float time, float speed, int alive, int dead, int s, int e, int i, int r, int v, int hospUsed, int hospTotal, int vacLeft, int vacTotal)
    {
        timeLabel.text = $"Day {day}  |  {Mathf.FloorToInt(time):00}:{Mathf.FloorToInt((time - Mathf.FloorToInt(time)) * 60f):00}";
        speedLabel.text = $"{speed}x";
        
        aliveLabel.text = alive.ToString("N0"); deadLabel.text = dead.ToString("N0");
        susceptibleLabel.text = s.ToString("N0"); exposedLabel.text = e.ToString("N0");
        infectedLabel.text = i.ToString("N0"); recoveredLabel.text = r.ToString("N0");
        vaccinatedLabel.text = v.ToString("N0");

        hospitalLabel.text = $"{hospUsed} / {hospTotal}";
        vaccineLabel.text = $"{vacLeft} / {vacTotal}";
    }
}

// ==========================================
// WIDGET 5: Virus & Vaccine Controls 
// ==========================================
public class VirusVaccineControls : VisualElement
{
    public new class UxmlFactory : UxmlFactory<VirusVaccineControls, UxmlTraits> { }

    public System.Action<int, float, float, float, float> OnMutateVirus;
    public System.Action<int, int, float, float> OnDeployVaccine;

    public VirusVaccineControls()
    {
        style.paddingTop = 10; style.paddingBottom = 10;
        style.flexDirection = FlexDirection.Column;

        var mainTitle = new Label("INTERVENTION CONTROLS") { style = { color = Color.white, fontSize = 13, unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 12, unityTextAlign = TextAnchor.MiddleCenter } };
        Add(mainTitle);

        // --- VIRUS FOLDOUT ---
        var virusFoldout = new Foldout { text = "Virus Mutation Parameters" };
        virusFoldout.AddToClassList("custom-foldout"); 
        
        var virusContent = new VisualElement(); 
        
        var vStrainField = new IntegerField("New Strain Level:") { value = 2 };
        var vEvasionSlider = new Slider("Antigenic Shift (Evasion):", 0f, 1f) { value = 0.30f };
        var vTransSlider = new Slider("Transmissibility (\u03B2):", 0.01f, 1.0f) { value = 0.40f };
        var vIncubationSlider = new Slider("Incubation (Days):", 1f, 14f) { value = 5f };
        var vFatalitySlider = new Slider("Fatality Rate (IFR):", 0f, 0.15f) { value = 0.02f };
        
        vStrainField.AddToClassList("inspector-field");
        vEvasionSlider.AddToClassList("inspector-field");
        vTransSlider.AddToClassList("inspector-field");
        vIncubationSlider.AddToClassList("inspector-field");
        vFatalitySlider.AddToClassList("inspector-field");

        var mutateBtn = new Button(() => { OnMutateVirus?.Invoke(vStrainField.value, vEvasionSlider.value, vTransSlider.value, vIncubationSlider.value, vFatalitySlider.value); }) { text = "TRIGGER MUTATION" };
        mutateBtn.AddToClassList("action-button"); mutateBtn.AddToClassList("action-button--danger");
        mutateBtn.style.marginTop = 10; mutateBtn.style.marginBottom = 10;
        
        virusContent.Add(vStrainField); virusContent.Add(vEvasionSlider); virusContent.Add(vTransSlider); virusContent.Add(vIncubationSlider); virusContent.Add(vFatalitySlider); virusContent.Add(mutateBtn);
        virusFoldout.Add(virusContent); Add(virusFoldout);

        // --- VACCINE FOLDOUT ---
        var vaccineFoldout = new Foldout { text = "Vaccine Deployment" };
        vaccineFoldout.AddToClassList("custom-foldout"); 

        var vaccineContent = new VisualElement(); 

        var vacStrainField = new IntegerField("Vaccine Strain:") { value = 1 };
        var vacDosesField = new IntegerField("Supply Doses:") { value = 5000 };
        var vacEfficacySlider = new Slider("Base Efficacy:", 0f, 1f) { value = 0.90f };
        var vacAbidanceSlider = new Slider("Public Abidance:", 0f, 1f) { value = 0.75f };
        
        vacStrainField.AddToClassList("inspector-field"); vacDosesField.AddToClassList("inspector-field");
        vacEfficacySlider.AddToClassList("inspector-field"); vacAbidanceSlider.AddToClassList("inspector-field");

        var deployBtn = new Button(() => { OnDeployVaccine?.Invoke(vacStrainField.value, vacDosesField.value, vacEfficacySlider.value, vacAbidanceSlider.value); }) { text = "DEPLOY WAVE" };
        deployBtn.AddToClassList("action-button"); deployBtn.AddToClassList("action-button--primary");
        deployBtn.style.marginTop = 10; deployBtn.style.marginBottom = 10;

        vaccineContent.Add(vacStrainField); vaccineContent.Add(vacDosesField); vaccineContent.Add(vacEfficacySlider); vaccineContent.Add(vacAbidanceSlider); vaccineContent.Add(deployBtn);
        vaccineFoldout.Add(vaccineContent); Add(vaccineFoldout);
        
        virusFoldout.value = true; vaccineFoldout.value = true;
    }
}