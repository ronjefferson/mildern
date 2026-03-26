using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

// ==========================================
// GRAPH 1: The Classic SIRD Line Graph
// ==========================================
public class EpidemicLineGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<EpidemicLineGraph, UxmlTraits> { }

    private List<float> sData = new List<float>(), iData = new List<float>(), rData = new List<float>(), dData = new List<float>();
    public float maxPopulation = 10000f;
    public int maxDataPoints = 2000;

    private Label yMaxLabel;
    private Label xMaxLabel;

    public EpidemicLineGraph()
    {
        style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
        
        var yAxisTitle = new Label("Y: Population") { style = { position = Position.Absolute, top = 5, left = 5, color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10 } };
        yMaxLabel = new Label("Max: 0") { style = { position = Position.Absolute, top = 20, left = 5, color = Color.white, fontSize = 10 } };
        
        var xAxisTitle = new Label("X: Time (Days)") { style = { position = Position.Absolute, bottom = 2, left = 40, color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10 } };
        xMaxLabel = new Label("Day: 0") { style = { position = Position.Absolute, bottom = 2, right = 10, color = Color.white, fontSize = 10 } };
        
        var zeroLabel = new Label("0") { style = { position = Position.Absolute, bottom = 20, left = 20, color = Color.gray, fontSize = 10 } };
        
        Add(yAxisTitle); Add(yMaxLabel);
        Add(xAxisTitle); Add(xMaxLabel);
        Add(zeroLabel);

        var legendRow = new VisualElement() { style = { flexDirection = FlexDirection.Row, position = Position.Absolute, top = 5, right = 10 } };
        legendRow.Add(CreateLegendItem(Color.cyan, "Susceptible"));
        legendRow.Add(CreateLegendItem(Color.red, "Infected"));
        legendRow.Add(CreateLegendItem(Color.green, "Recovered"));
        legendRow.Add(CreateLegendItem(Color.gray, "Dead"));
        Add(legendRow);

        generateVisualContent += DrawGraph;
    }

    private VisualElement CreateLegendItem(Color c, string text)
    {
        var row = new VisualElement() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 10 } };
        var box = new VisualElement() { style = { width = 10, height = 10, backgroundColor = c, marginRight = 4 } };
        var lbl = new Label(text) { style = { color = Color.white, fontSize = 10 } };
        row.Add(box); row.Add(lbl);
        return row;
    }

    public void AddData(float s, float i, float r, float d, int currentDay)
    {
        sData.Add(s); iData.Add(i); rData.Add(r); dData.Add(d);
        if (sData.Count > maxDataPoints) { sData.RemoveAt(0); iData.RemoveAt(0); rData.RemoveAt(0); dData.RemoveAt(0); }
        
        yMaxLabel.text = $"Max: {maxPopulation}";
        xMaxLabel.text = $"Current Day: {currentDay}";
        MarkDirtyRepaint();
    }

    void DrawGraph(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        float w = contentRect.width, h = contentRect.height;
        
        float padL = 40f, padR = 10f, padT = 30f, padB = 20f;
        float graphW = w - padL - padR;
        float graphH = h - padT - padB;

        // DRAW AXES FIRST (So they show up in the Editor before Play mode!)
        p.strokeColor = new Color(0.4f, 0.4f, 0.4f, 1f); 
        p.lineWidth = 1f; p.BeginPath();
        p.MoveTo(new Vector2(padL, padT)); 
        p.LineTo(new Vector2(padL, h - padB)); 
        p.LineTo(new Vector2(w - padR, h - padB)); 
        p.Stroke();

        // NOW check if we have enough data to draw the lines
        if (sData.Count < 2) return;

        float stepX = graphW / Mathf.Max(1, sData.Count - 1);

        DrawLine(p, sData, Color.cyan, stepX, graphH, maxPopulation, padL, padB, h);
        DrawLine(p, iData, Color.red, stepX, graphH, maxPopulation, padL, padB, h);
        DrawLine(p, rData, Color.green, stepX, graphH, maxPopulation, padL, padB, h);
        DrawLine(p, dData, Color.gray, stepX, graphH, maxPopulation, padL, padB, h);
    }

    private void DrawLine(Painter2D p, List<float> data, Color color, float stepX, float graphH, float maxY, float padL, float padB, float totalH)
    {
        p.strokeColor = color; p.lineWidth = 2f; p.BeginPath();
        for (int i = 0; i < data.Count; i++)
        {
            float x = padL + (i * stepX);
            float y = (totalH - padB) - ((data[i] / maxY) * graphH);
            if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
        }
        p.Stroke();
    }
}

// ==========================================
// GRAPH 2: The Current Snapshot Bar Graph
// ==========================================
public class EpidemicBarGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<EpidemicBarGraph, UxmlTraits> { }

    private float curS, curI, curR, curD;
    public float maxPopulation = 10000f;

    private Label yMaxLabel;

    public EpidemicBarGraph()
    {
        style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
        
        var titleLabel = new Label("Current Snapshot") { style = { position = Position.Absolute, top = 5, left = 5, color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10 } };
        yMaxLabel = new Label("Max: 0") { style = { position = Position.Absolute, top = 20, left = 5, color = Color.white, fontSize = 10 } };
        var zeroLabel = new Label("0") { style = { position = Position.Absolute, bottom = 20, left = 20, color = Color.gray, fontSize = 10 } };

        Add(titleLabel); Add(yMaxLabel); Add(zeroLabel);

        var legendRow = new VisualElement() { style = { flexDirection = FlexDirection.Row, position = Position.Absolute, top = 5, right = 10 } };
        legendRow.Add(CreateLegendItem(Color.cyan, "S"));
        legendRow.Add(CreateLegendItem(Color.red, "I"));
        legendRow.Add(CreateLegendItem(Color.green, "R"));
        legendRow.Add(CreateLegendItem(Color.gray, "D"));
        Add(legendRow);

        generateVisualContent += DrawBars;
    }

    private VisualElement CreateLegendItem(Color c, string text)
    {
        var row = new VisualElement() { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 8 } };
        var box = new VisualElement() { style = { width = 10, height = 10, backgroundColor = c, marginRight = 4 } };
        var lbl = new Label(text) { style = { color = Color.white, fontSize = 10 } };
        row.Add(box); row.Add(lbl);
        return row;
    }

    public void UpdateData(float s, float i, float r, float d)
    {
        curS = s; curI = i; curR = r; curD = d;
        yMaxLabel.text = $"Max: {maxPopulation}";
        MarkDirtyRepaint();
    }

    void DrawBars(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        float w = contentRect.width, h = contentRect.height;
        
        float padL = 40f, padR = 10f, padT = 30f, padB = 20f;
        float graphW = w - padL - padR;
        float graphH = h - padT - padB;

        p.strokeColor = new Color(0.4f, 0.4f, 0.4f, 1f); 
        p.lineWidth = 1f; p.BeginPath();
        p.MoveTo(new Vector2(padL, padT)); 
        p.LineTo(new Vector2(padL, h - padB)); 
        p.LineTo(new Vector2(w - padR, h - padB)); 
        p.Stroke();
        
        float barWidth = (graphW / 4f) * 0.7f; 
        float spacing = (graphW / 4f) * 0.3f;

        DrawSingleBar(p, curS, 0, barWidth, spacing, graphH, padL, padB, h, Color.cyan);
        DrawSingleBar(p, curI, 1, barWidth, spacing, graphH, padL, padB, h, Color.red);
        DrawSingleBar(p, curR, 2, barWidth, spacing, graphH, padL, padB, h, Color.green);
        DrawSingleBar(p, curD, 3, barWidth, spacing, graphH, padL, padB, h, Color.gray);
    }

    void DrawSingleBar(Painter2D p, float val, int index, float bW, float space, float graphH, float padL, float padB, float totalH, Color c)
    {
        float barHeight = (val / maxPopulation) * graphH;
        float x = padL + (index * bW) + (index * space) + (space / 2f);
        float y = (totalH - padB) - barHeight;

        p.fillColor = c;
        p.BeginPath();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + bW, y));
        p.LineTo(new Vector2(x + bW, totalH - padB));
        p.LineTo(new Vector2(x, totalH - padB));
        p.ClosePath();
        p.Fill();
    }
}

// ==========================================
// GRAPH 3: Dynamic Active Cases Graph
// ==========================================
public class ActiveCasesGraph : VisualElement
{
    public new class UxmlFactory : UxmlFactory<ActiveCasesGraph, UxmlTraits> { }

    private List<float> activeData = new List<float>();
    public int maxDataPoints = 2000;
    
    private Label maxValLabel;
    private float currentMaxY = 10f;

    public ActiveCasesGraph()
    {
        style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 1f));
        
        var yAxisTitle = new Label("Y: Active Infections") { style = { position = Position.Absolute, top = 5, left = 5, color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10 } };
        maxValLabel = new Label("Max: 0") { style = { position = Position.Absolute, top = 20, left = 5, color = Color.red, fontSize = 10 } };
        var zeroLabel = new Label("0") { style = { position = Position.Absolute, bottom = 20, left = 20, color = Color.gray, fontSize = 10 } };
        var xAxisTitle = new Label("X: Time (Zoomed)") { style = { position = Position.Absolute, bottom = 2, left = 40, color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10 } };

        Add(yAxisTitle); Add(maxValLabel); Add(zeroLabel); Add(xAxisTitle);

        var legendRow = new VisualElement() { style = { flexDirection = FlexDirection.Row, position = Position.Absolute, top = 5, right = 10 } };
        var box = new VisualElement() { style = { width = 10, height = 10, backgroundColor = Color.red, marginRight = 4 } };
        var lbl = new Label("Active Cases") { style = { color = Color.white, fontSize = 10 } };
        legendRow.Add(box); legendRow.Add(lbl);
        Add(legendRow);

        generateVisualContent += DrawGraph;
    }

    public void AddData(float iCount)
    {
        activeData.Add(iCount);
        if (activeData.Count > maxDataPoints) activeData.RemoveAt(0);

        if (activeData.Count >= 2)
        {
            currentMaxY = activeData.Max();
            if (currentMaxY < 10) currentMaxY = 10; 
            maxValLabel.text = $"Max: {Mathf.CeilToInt(currentMaxY)}";
        }

        MarkDirtyRepaint();
    }

    void DrawGraph(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        float w = contentRect.width, h = contentRect.height;
        
        float padL = 40f, padR = 10f, padT = 30f, padB = 20f;
        float graphW = w - padL - padR;
        float graphH = h - padT - padB;

        // DRAW AXES FIRST
        p.strokeColor = new Color(0.4f, 0.4f, 0.4f, 1f); 
        p.lineWidth = 1f; p.BeginPath();
        p.MoveTo(new Vector2(padL, padT)); 
        p.LineTo(new Vector2(padL, h - padB)); 
        p.LineTo(new Vector2(w - padR, h - padB)); 
        p.Stroke();

        // THEN check if there is data
        if (activeData.Count < 2) return;

        float stepX = graphW / Mathf.Max(1, activeData.Count - 1);

        p.fillColor = new Color(1f, 0f, 0f, 0.2f);
        p.BeginPath();
        p.MoveTo(new Vector2(padL, h - padB)); 
        for (int i = 0; i < activeData.Count; i++)
        {
            float x = padL + (i * stepX);
            float y = (h - padB) - ((activeData[i] / currentMaxY) * graphH);
            p.LineTo(new Vector2(x, y));
        }
        p.LineTo(new Vector2(padL + ((activeData.Count - 1) * stepX), h - padB)); 
        p.ClosePath();
        p.Fill();

        p.strokeColor = Color.red; p.lineWidth = 2f; p.BeginPath();
        for (int i = 0; i < activeData.Count; i++)
        {
            float x = padL + (i * stepX);
            float y = (h - padB) - ((activeData[i] / currentMaxY) * graphH);
            if (i == 0) p.MoveTo(new Vector2(x, y)); else p.LineTo(new Vector2(x, y));
        }
        p.Stroke();
    }
}