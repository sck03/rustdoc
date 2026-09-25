// Editable report documents. All geometry is in millimetres at this authoring
// boundary; the shared model stores integer hundredths of a millimetre.
const sans = "Noto Sans CJK SC", serif = "Noto Serif CJK SC";
const mm = n => Math.round(n * 100);
const border = { color: "#000000", widthPx: 0.6, style: "Solid", top: true, right: true, bottom: true, left: true };
const noBorder = { ...border, widthPx: 0, style: "None" };
function document(reportType, landscape = false, font = sans) {
  return {
    version: 3, astKind: "ReportDocument", coordinateUnit: "hundredth-mm", contractVersion: "3.0", reportType,
    page: { size: "A4", orientation: landscape ? "Landscape" : "Portrait", widthHundredthMm: mm(landscape ? 297 : 210), heightHundredthMm: mm(landscape ? 210 : 297), marginTopHundredthMm: 1000, marginRightHundredthMm: 1000, marginBottomHundredthMm: 1000, marginLeftHundredthMm: 1000, fontFamily: font, fontSizePt: 9 },
    grid: { enabled: true, snap: true, sizeHundredthMm: 100 },
    layers: ["Header", "Body", "Footer", "Overlay"].map(role => ({ id: role.toLowerCase(), name: {Header:"页眉",Body:"主体",Footer:"页脚",Overlay:"覆盖层"}[role], role, visible: true, locked: false, print: { repeatOnEveryPage: role === "Header" || role === "Footer", keepTogether: role !== "Body", pinToPageBottom: false, minHeightHundredthMm: 0 }, elements: [] })),
  };
}
function element(id, type, x, y, w, h, value, size = 9, align = "Left", bold = false, font = sans) {
  return { id, label: id, type, xHundredthMm:mm(x), yHundredthMm:mm(y), widthHundredthMm:mm(w), heightHundredthMm:mm(Math.max(4,h)), rotationDeg:0, zIndex:0, visible:true, locked:false, outputEnabled:true,
    style: { fontFamily:font, fontSizePt:size, bold, align, verticalAlign:"Top", color:"#000000", backgroundColor:"#ffffff", borderColor:"#000000", borderWidthPx:0, borderStyle:"None", paddingHundredthMm:30 }, ...value };
}
const text = (id, value, x,y,w,h,size=9,align="Left",bold=false,font=sans) => element(id,"Text",x,y,w,h,{text:value},size,align,bold,font);
const field = (id, path, x,y,w,h,size=9,align="Left",bold=false,font=sans) => element(id,"Field",x,y,w,h,{fieldPath:path,fallbackText:""},size,align,bold,font);
const seal = (id,path,x,y,w,h) => element(id,"Image",x,y,w,h,{sourceKind:"Field",purpose:"Stamp",fieldPath:path,resourceId:"",altText:"",hideWhenSourceEmpty:true});
// y is the printed line position; the editor box follows the 4 mm contract.
const rule = (id,x,y,w) => ({...element(id,"Line",x,y-2,w,4,{direction:"Horizontal"}),style:{borderColor:"#000000",borderWidthPx:0.8,borderStyle:"Dashed"}});
function pageNumber(x=95,y=280) { return element("page-number","PageNumber",x,y,25,5,{format:"CurrentOfTotal",prefix:"",suffix:""},7,"Center"); }
function cell(id, value, span=1, options={}) {
  return { id, colSpan:span, rowSpan:1, contentKind:"Text", text:value, label:"", fieldPath:"", fallbackText:"", verticalText:false, checkboxOptions:[], style:{}, ...options };
}
const binding = (id,path,span=1,options={}) => cell(id,"",span,{contentKind:"Field",fieldPath:path,...options});
function grid(id,x,y,w,widths,rows,size=9,font=serif,lines=true) {
  const h = rows.reduce((sum,row)=>sum+row[0],0);
  return element(id,"Flow",x,y,w,h,{flowKind:"Grid",block:{ id:`${id}-block`,type:"Grid",title:"", columns:widths.map((n,i)=>({id:`${id}-c${i}`,widthPercent:n})), rows:rows.map(([height,cells],i)=>({id:`${id}-r${i}`,heightMm:height,cells})), border:lines?border:noBorder, defaultCellStyle:{fontSizePt:size,align:"Left",verticalAlign:"Middle",marginTopMm:0.6,marginBottomMm:0.6,marginLeftMm:1,marginRightMm:1}}},size,"Left",false,font);
}
const part = (kind,value) => ({kind,...(kind==="Field"?{fieldPath:value}:kind==="Text"?{text:value}:{})});
const F = value => part("Field",value), T = value => part("Text",value), NL = () => part("LineBreak");
function column(id,title,path,width,align="Left",parts=null) {
  return {id,title,fieldPath:path,widthMm:width,align,headerGroupSpan:1,contentKind:parts?"Composite":"Field", ...(parts?{content:parts.map((p,i)=>({id:`${id}-p${i}`,...p}))}:{})};
}
function table(id,x,y,w,columns,size=8,first=12,next=12) {
  return element(id,"Flow",x,y,w,190,{flowKind:"DetailTable",block:{id:`${id}-block`,type:"DetailTable",title:"",sourcePath:"Invoice.Items",repeatMode:"ScribanFor",columns,print:{repeatHeaderOnPageBreak:true,keepRowsTogether:true,firstPageRows:first,continuationPageRows:next},headerStyle:{fontSizePt:size,bold:true,align:"Center"},bodyStyle:{fontSizePt:size},border}},size);
}
function summary(table,cells,label="TOTAL:") {
  table.block.summaryRow={label,labelColumnSpan:1,style:{fontSizePt:7,bold:true},cells:Object.entries(cells).map(([columnId,fieldPath])=>({columnId,contentKind:"Field",fieldPath,text:""}))};
}
function commercialHeader(d,title) {
  d.layers[0].elements=[
    field("company","Exporter.ExporterNameEN",15,16,180,7,12,"Center",true),
    field("company-address","Exporter.AddressEN",15,23,180,8,7,"Center"),
    text("title",title,15,33,180,7,11,"Center",true),
    text("to","TO: M/S",15,44,85,5,8),
    field("customer","Customer.CustomerNameEN",15,49,107,6,8),
    field("customer-address","Customer.AddressEN",15,55,107,9,7),
    text("invoice-label","Invoice No.:",133,44,29,5,7),field("invoice-no","Invoice.InvoiceNo",163,44,32,5,7,"Right"),
    text("contract-label","Contract No.:",133,50,29,5,7),field("contract-no","Invoice.ContractNo",163,50,32,5,7,"Right"),
    text("date-label","Date:",133,56,29,5,7),field("invoice-date","Invoice.InvoiceDate",163,56,32,5,7,"Right"),
  ];
  d.layers[2].elements=[pageNumber()];
  const final={id:"final",name:"末页签章",role:"Footer",visible:true,locked:false,print:{repeatOnEveryPage:false,keepTogether:true,pinToPageBottom:false,minHeightHundredthMm:0},elements:[seal("seal","doc_seal_path",150,250,40,25)]};
  d.layers.push(final);
}
function invoice() {
  const d=document("ExportDocument"); commercialHeader(d,"INVOICE");
  d.layers[0].elements.push(
    text("loading-label","From:",15,65,11,5,7),field("loading","Invoice.PortOfLoading",26,65,79,5,7),rule("loading-rule",26,70,79),
    text("destination-label","To:",105,65,10,5,7),field("destination","Invoice.PortOfDestination",115,65,80,5,7),rule("destination-rule",115,70,80),
    text("terms-label","Payment Terms:",15,71,21,5,7),field("terms","Invoice.PaymentTerms",36,71,69,5,7),rule("terms-rule",36,76,69),
    text("issuer-label","Issued by:",105,71,15,5,7),text("issuer","",120,71,75,5,7),rule("issuer-rule",120,76,75),
  );
  // One editable product row. Document fields, rules and totals have their own layers.
  const namedField=(id,label,path,x,y,w,h=4,align="Left",bold=false)=>({...field(id,path,x,y,w,h,7,align,bold),label});
  d.detailRowHeightHundredthMm=mm(12);
  d.layers[0].elements.push(
    grid("invoice-headings",15,78,180,[32/1.8,115/1.8,33/1.8],[[8,[cell("marks-heading","唛头 / Marks"),cell("goods-heading","货品名称 / Quantities and Descriptions"),cell("amount-heading","金额 / Amount")]]],7,sans),
    namedField("currency","币种","Invoice.Currency",164,92,29,4,"Right"),
  );
  d.layers[1].elements=[
    namedField("item-name","货品名称","item.StyleName",49,100,34),
    namedField("item-po","客户 PO","item.PoNumber",49,104,34),
    namedField("item-style","款号","item.StyleNo",49,108,34),
    namedField("item-cartons","箱数","item.Cartons",85,108,10),
    namedField("item-carton-unit","箱数单位","item.CtnUnitEN",95,108,12),
    namedField("item-quantity","数量","item.Quantity",108,108,12),
    namedField("item-quantity-unit","数量单位","item.UnitEN",120,108,12),
    namedField("item-price","单价","item.UnitPrice",134,108,26,4,"Right"),
    namedField("item-amount","金额","item.TotalPrice",164,108,29,4,"Right"),
  ];
  for(const e of d.layers[1].elements) if(e.yHundredthMm===mm(108)) e.style.verticalAlign="Bottom";
  const first={...d.layers[0],id:"invoice-first",name:"首页单据信息",print:{...d.layers[0].print,firstPageOnly:true},elements:[
    namedField("shipping-marks","唛头","Invoice.ShippingMarks",16,100,30,100),
    namedField("trade-terms","价格条款","Invoice.TradeTerms",164,86,29,5,"Right"),
  ]};
  const lines=[15,47,162,195].map((x,i)=>({...element(`invoice-rule-${i}`,"Line",x-2,86,4,159,{direction:"Vertical"}),style:{borderStyle:"Solid",borderWidthPx:0.6,borderColor:"#000000"}}));
  d.layers[3].elements=[...lines,{...rule("invoice-bottom",15,245,180),style:{borderStyle:"Solid",borderWidthPx:0.6,borderColor:"#000000"}}];
  const final=d.layers.find(layer=>layer.id==="final");
  final.name="末页合计与签章";
  final.print.followBody=true;
  final.elements=[
    rule("total-rule",47,117,148),text("total-label","TOTAL:",49,121,34,9,7,"Left",true),
    namedField("total-cartons","总箱数","total_by_ctn_unit",85,121,22,9,"Left",true),
    namedField("total-quantity","总数量","total_by_qty_unit",108,121,24,9,"Left",true),
    namedField("total-amount","总金额","Invoice.TotalAmount",164,121,29,9,"Right",true),
    seal("seal","doc_seal_path",150,134,40,25),
  ];
  d.layers.push(first);
  const labels={company:"公司名称", "company-address":"公司地址",customer:"客户名称","customer-address":"客户地址","invoice-no":"发票号码","contract-no":"合同号码","invoice-date":"发票日期",loading:"起运港",destination:"目的港",terms:"付款条件"};
  for(const layer of d.layers) for(const e of layer.elements) if(labels[e.id]) e.label=labels[e.id];
  return d;
}
function packing() {
  const d=document("ExportDocument"); commercialHeader(d,"PACKING LIST");
  const t=table("packing-items",15,65,180,[
    column("description","货品名称 / Description","item.StyleNo",50,"Left",[F("item.StyleName"),NL(),F("item.PoNumber"),NL(),F("item.StyleNo")]),
    column("cartons","箱数\nCartons","item.Cartons",18,"Right",[F("item.Cartons"),F("item.CtnUnitEN")]),
    column("quantity","数量\nQuantity","item.Quantity",22,"Right",[F("item.Quantity"),F("item.UnitEN")]),
    column("gross","毛重\nGross Weight","item.GWTotal",20,"Right",[F("item.GWTotal"),T(" KGS")]),
    column("net","净重\nNet Weight","item.NWTotal",20,"Right",[F("item.NWTotal"),T(" KGS")]),
    column("volume","体积\nMEAS.","item.Volume",18,"Right",[F("item.Volume"),T(" CBM")]),
  ],6.5);
  t.heightHundredthMm=mm(165);
  t.block.sideBand={title:"唛头 / Marks",widthMm:32,contentKind:"Field",text:"",fieldPath:"Invoice.ShippingMarks",style:{fontSizePt:7}};
  t.block.columns[0].omitEmptyLines=true;
  t.block.sideBand.firstPageOnly=true;
  t.block.rowSeparators=false;
  t.block.bodyStyle.verticalAlign="Bottom";
  summary(t,{cartons:"total_by_ctn_unit",quantity:"total_by_qty_unit",gross:"Invoice.TotalGrossWeight",net:"Invoice.TotalNetWeight",volume:"Invoice.TotalVolume"});
  t.block.summaryRow.style.marginTopMm=2.5;
  d.layers[1].elements=[t]; return d;
}
function contract() {
  const d=document("ExportDocument");
  d.layers[0].print.repeatOnEveryPage=false;
  d.layers[0].elements=[field("company","Exporter.ExporterNameEN",15,16,180,7,12,"Center",true),text("title","售货合同",15,25,180,7,11,"Center",true),text("buyer-label","买方 / Buyer:",15,35,90,5,8),field("buyer","Customer.CustomerNameEN",15,41,95,6,8),field("buyer-address","Customer.AddressEN",15,48,95,10,7),text("seller-label","卖方 / Seller:",15,60,95,5,8),field("seller","Exporter.ExporterNameEN",15,65,180,6,8),text("contract-label","合同号码 (Contract No.):",116,41,39,6,8),field("contract-no","Invoice.ContractNo",155,41,40,6,8,"Right"),text("date-label","日期 (Date):",116,57,35,5,8),field("date","Invoice.InvoiceDate",151,57,44,5,8,"Right"),text("agreement","双方同意按本合同所列条款由卖方出售，买方购进下列货物：\nThe sellers agree to sell and the Buyers agree to buy the under-mentioned commodities on the terms and conditions stated below:",15,76,180,10,7)];
  const t=table("contract-items",15,88,180,[column("goods","Name of Commodity, Specification, Packing and Shipping Marks","item.StyleNo",110,"Left",[F("item.StyleNo"),T(" "),F("item.StyleName")]),column("quantity","Quantity","item.Quantity",23,"Right",[F("item.Quantity"),F("item.UnitEN")]),column("price","Unit Price","item.UnitPrice",23,"Right",[F("Invoice.Currency"),F("item.UnitPrice")]),column("amount","Total Amount","item.TotalPrice",24,"Right",[F("Invoice.Currency"),F("item.TotalPrice")])],7,20,20);
  t.block.print.firstPageRows=24; t.block.print.continuationPageRows=40;
  t.heightHundredthMm=mm(40); summary(t,{quantity:"total_by_qty_unit",amount:"Invoice.TotalAmount"});
  d.layers[1].elements=[t,
    grid("delivery",15,132,180,[20,25,25,30],[[10,[binding("shipment","Invoice.ShipmentDate",2,{label:"装运期限 / Time of shipment"}),cell("partial","装运港允许分批装运\nShipment quantity 5% more or less allowed",2)]],[10,[binding("loading","Invoice.PortOfLoading",2,{label:"装运港 / Port of loading"}),binding("destination","Invoice.PortOfDestination",2,{label:"目的港 / Port of destination"})]]],7,sans,false),
    grid("clauses",15,154,180,[100],[[32,[cell("delivery-insurance","(9)交货条件：FOB/CFR/CIF 若无另外规定均按照《国际贸易术语解释通则（ TNCOTERMS）1990》办理。\nTerms of delivery: FOB/CFR/CIF shall conform to 《INCOTERMS1990》unless otherwise agreed.\n(10)保 险：由卖方按发票总值的110%投保一切险加战争险，如买方欲增加其他险别或超过上述额度保险时须事先征得卖方同意，增加的保费由买方承担。\nInsurance: To be covered by the sellers for 110% of the total value against, all risks and war risks. Should the Buyers desire to cover for other risks besides the above mentioned or for an amount exceeding the above mentioned limit. The sellers’ approval must be obtained first and all additional premium charges incurred therewith shall be for buyers’ account.")]], [9,[binding("payment","Invoice.PaymentTerms",1,{label:"(11)付款条件 / Terms of payment"})]], [12,[binding("special","Invoice.SpecialTerms")]]],7,sans,false),
    grid("signatures",15,211,180,[50,50],[[10,[cell("buyer-sign","买方签字：\nThe buyers' signature:"),cell("seller-sign","卖方签字：\nThe sellers' signature:")]]],7,sans,false),
  ];
  const contractTail = d.layers[1].elements.splice(1);
  d.layers[2].elements=[pageNumber()];
  d.layers.push({id:"seal-layer",name:"末页印章",role:"Footer",visible:true,locked:false,print:{repeatOnEveryPage:false,keepTogether:true,pinToPageBottom:false,followBody:true,minHeightHundredthMm:0},elements:[...contractTail,seal("seal","doc_seal_path",150,223,40,25)]});
  return d;
}
function customs() {
  const d=document("ExportDocument",true,serif);
  d.page.marginTopHundredthMm=2400;
  d.layers[0].print.repeatOnEveryPage=false;
  d.layers[0].elements=[text("title","中华人民共和国海关出口货物报关单",10,11,277,7,12,"Center",true),text("pre-entry","预录入编号：",10,19,80,4,7),text("customs-no","海关编号：",95,19,85,4,7)];
  const declarationWidths=[49,94,41,82,34,68,76,103,49,76,77,50,35,103,100];
  const declarationTotal=declarationWidths.reduce((a,b)=>a+b,0);
  const declarationColumns=declarationWidths.map(n=>n/declarationTotal*100);
  d.layers[0].elements.push(grid("declaration-header",10,24,277,declarationColumns,[
    [9,[binding("exporter","Exporter.ExporterNameCN",5,{label:"境内发货人"}),cell("customs","出境关别",2),binding("export-date","Invoice.ShipmentDate",3,{label:"出口日期"}),cell("declare-date","申报日期",3),cell("record-no","备案号",2)]],
    [9,[binding("customer","Customer.CustomerNameEN",5,{label:"境外收货人"}),binding("mode","Invoice.TransportMode",2,{label:"运输方式"}),cell("vehicle","运输工具名称及航次号",3),cell("bill","提运单号",5)]],
    [9,[binding("producer","Exporter.ExporterNameCN",5,{label:"生产销售单位"}),binding("supervision","Invoice.SupervisionMode",2,{label:"监管方式"}),cell("tax","征免性质",3),cell("license","许可证号",5)]],
    [9,[binding("contract","Invoice.ContractNo",5,{label:"合同协议号"}),binding("trade-country","Invoice.DestinationCountry",2,{label:"贸易国(地区)"}),binding("destination","Invoice.DestinationCountry",3,{label:"运抵国(地区)"}),binding("port","Invoice.PortOfDestination",3,{label:"指运港"}),binding("loading","Invoice.PortOfLoading",2,{label:"离境口岸"})]],
    [9,[cell("package","包装种类",5),binding("cartons","Invoice.TotalCartons",1,{label:"件数"}),binding("gross","Invoice.TotalGrossWeight",1,{label:"毛重(千克)"}),binding("net","Invoice.TotalNetWeight",2,{label:"净重(千克)"}),binding("trade","Invoice.TradeTerms",1,{label:"成交方式"}),cell("freight","运费",2),cell("insurance","保费",2),cell("misc","杂费",1)]],
    [4,[cell("documents","随附单证",15)]],[6,[cell("marks","标记唛码及备注",15)]],
  ],6,serif));
  for (const row of d.layers[0].elements.at(-1).block.rows) {
    for (const cell of row.cells) if (cell.contentKind === "Field") { cell.labelPosition="Above"; cell.style.verticalAlign="Top"; }
  }
  d.layers[0].elements.push(field("exporter-credit","Exporter.CreditCode",29,24.3,55,2.4,6.5),field("producer-credit","Exporter.CreditCode",29,42.3,55,2.4,6.5));
  const cols=[column("index","项号","item.RowNumber",12,"Center"),column("hs","商品编号","item.HsCode",32),column("goods","商品名称及规格型号","item.StyleNameCN",90,"Left",[F("item.StyleNameCN"),NL(),F("item.FabricComposition"),T(" 品牌: "),F("item.Brand")]),column("quantity","数量及单位","item.Quantity",32,"Right",[F("item.Quantity"),F("item.UnitCN"),NL(),F("item.NWTotal"),T("千克")]),column("amount","单价/总价/币制","item.UnitPrice",35,"Right",[F("Invoice.Currency"),F("item.UnitPrice"),NL(),F("Invoice.Currency"),F("item.TotalPrice")]),column("origin-country","原产国(地区)","item.Origin",22,"Center",[T("中国")]),column("destination-country","最终目的国(地区)","item.Origin",27,"Center",[F("Invoice.DestinationCountry")]),column("origin","境内货源地","item.Origin",27,"Center"),column("exemption","征免","item.Origin",12,"Center",[T("")])];
  const itemSpans=[1,2,4,1,2,1,2,1,1]; let itemColumn=0;
  cols.forEach((col,i)=>{col.widthMm=declarationWidths.slice(itemColumn,itemColumn+itemSpans[i]).reduce((a,b)=>a+b,0)/declarationTotal*277;itemColumn+=itemSpans[i];});
  const t=table("customs-items",10,79,277,cols,6.5,6,15); t.heightHundredthMm=mm(70);
  d.layers[1].elements=[t];
  const declaration=grid("declaration",10,155,277,declarationColumns,[
    [4,[cell("relation-space","",2),cell("relation","特殊关系确认：",3),cell("price-space",""),cell("price","价格影响确认：",2),cell("royalty-space","",2),cell("royalty","支付特许权使用费确认：",2),cell("tax-space",""),cell("self-tax","自报自缴：",2)]],
    [5,[cell("agent","报关人员"),cell("agent-no","报关人员证号",2),cell("agent-space",""),cell("phone","电话"),cell("phone-space","",2),cell("liability","兹申明对以上内容承担如实申报、依法纳税责任",5),cell("customs-sign","海关批注及签章",3)]],
    [5,[cell("company","申报单位"),cell("company-space","",9),cell("signature","申报单位(签章)",2),cell("sign-space","",3)]],
  ],6.5,serif);
  declaration.block.rows.forEach((row,rowIndex)=>{let col=0;row.cells.forEach(c=>{c.style={bold:true,align:rowIndex===0||c.id==="signature"?"Center":"Left"};c.border={...border,top:rowIndex===0,bottom:rowIndex===0||rowIndex===2,left:col===0||(rowIndex>0&&col===12),right:col+c.colSpan===15};col+=c.colSpan;});});
  d.layers[2].elements=[declaration,text("brand-note","境外品牌(贴牌生产)\n出口货物不能确定在最终目的国（地区）享受优惠",46,175,235,11,7,"Left",false,serif),seal("customs-seal","customs_seal_path",185,156,44,25)];
  d.layers[2].print.followBody=true;
  d.layers[2].print.firstPageOnly=true;
  d.layers[3].elements=[pageNumber(247,18)];
  return d;
}
function payment(expense) {
  const d=document("PaymentVoucher",false,serif);
  d.layers[0].elements=[field("payer","Payment.PayerName",15,10,180,6,13,"Center",true),text("title",expense?"费用报销明细单":"付款单（费用支付专用）",15,18,180,7,14,"Center",false,serif),text("department-label",expense?"业务科别：":"部门：",15,29,25,5,9,"Left",false,serif),field("department","Payment.Department",40,29,77,5,9,"Left",false,serif)];
  if(expense){
    d.layers[0].elements.push(field("date","Payment.PaymentDate",145,29,50,5,9,"Right",false,serif));
    const names=["差旅费","业务招\n待费","电话费","办公费","修理费","运杂费","检验费","其他"];
    const fields=["TravelExpense","BusinessEntertainmentExpense","TelephoneExpense","OfficeExpense","RepairExpense","FreightMiscExpense","InspectionExpense","OtherExpense"];
    d.layers[1].elements=[grid("expense-grid",15,37,180,[10,...Array(8).fill(11.25)],[
      [18,[cell("diagonal","",1,{diagonalHeader:{direction:"Down",upperLeftText:"项目",lowerRightText:""}}),...names.map((s,i)=>cell(`heading-${i}`,s,1,{style:{bold:true,align:"Center"}}))]],
      [12,[cell("amount-label","金额",1,{style:{align:"Center"}}),...fields.map((s,i)=>binding(`expense-${i}`,`Payment.${s}`,1,{style:{align:"Center"}}))]],
      [18,[cell("attachments","附件\n(张)",1,{style:{align:"Center"}}),...fields.map((_,i)=>cell(`attachment-${i}`,""))]],
      [12,[cell("notes-label","备注",1,{style:{align:"Center"}}),binding("notes","Payment.Notes",8)]],
      [11,[binding("upper","cny_amount_upper",6,{label:"报销净额"}),binding("total","Payment.CNYAmount",3,{label:"小计: ￥"})]],
    ],10,serif),grid("expense-signatures",15,111,180,[35,35,30],[[7,[cell("applicant","报销人："),cell("supervisor","主管签字："),cell("approval","审批签字：")]]],10,serif,false)];
  } else {
    d.layers[1].elements=[grid("payment-grid",14,34,170,[12,9,16,13,24,11,15],[
      [15,[cell("purpose","用款事项",1,{rowSpan:5,verticalText:true,style:{align:"Center",bold:true}}),cell("project-label","项目"),binding("project","Payment.Project"),cell("invoice-label","出口发票号码"),binding("invoice","Payment.InvoiceNo"),cell("shipment-label","出货日期"),binding("shipment","Payment.ShipmentDate")]],
      [15,[cell("usd-label","美元"),binding("usd","Payment.USDAmount"),cell("cny-label","人民币(大写)"),binding("cny-upper","cny_amount_upper"),cell("small","(小写)"),binding("cny","Payment.CNYAmount",1,{label:"￥"})]],
      [10,[binding("payee","Payment.PayeeName",3,{label:"支付单位名称"}),cell("method","",3,{contentKind:"CheckboxGroup",fieldPath:"Payment.PaymentMethod",checkboxOptions:["支票","电汇","预付"].map((name,i)=>({id:`method-${i}`,label:name,value:name})),style:{align:"Center"}})]],
      [9,[binding("bank","Payment.BankName",3,{label:"开户行"}),binding("notes","Payment.Notes",3,{rowSpan:2,label:"备注"})]],
      [9,[binding("account","Payment.AccountNo",3,{label:"账号"})]],
    ],8.5,serif),grid("payment-signatures",14,98,170,[35,30,35],[[7,[cell("manager","业务经理签字："),cell("approval","审批："),cell("review","复核：")]]],9,serif,false)];
    d.layers[3].elements=[text("stub","②\n付\n款\n联",189,48,8,33,10,"Center",true,serif)];
  }
  return d;
}
export function defaultReportDesigns() {
  return [
    ["Templates/Export/invoice_template.dtpl",invoice()],
    ["Templates/Export/packing_list_template.dtpl",packing()],
    ["Templates/Export/contract_template.dtpl",contract()],
    ["Templates/Export/customs_declaration_template.dtpl",customs()],
    ["Templates/Internal/payment_voucher_template.dtpl",payment(false)],
    ["Templates/Internal/expense_reimbursement_template.dtpl",payment(true)],
  ].map(([path, design]) => {
    const bodyTop = Math.min(...design.layers.filter(layer => layer.role === "Body").flatMap(layer => layer.elements.map(element => element.yHundredthMm)));
    for (const layer of design.layers) {
      if (layer.role === "Header") layer.designHeightHundredthMm = Number.isFinite(bodyTop) ? bodyTop : 1800;
      if (layer.role === "Footer") layer.designHeightHundredthMm = layer.elements.length ? design.page.heightHundredthMm - Math.min(...layer.elements.map(element => element.yHundredthMm)) : 0;
    }
    return [path, design];
  });
}
