use super::*;

const OBSERVED: &str = "2026-09-20T00:00:00+08:00";

#[test]
fn standard_table_keeps_description_separate_from_elements() {
    let html = r#"
    <div id="resultfind">您查询的相关hs编码 15 条</div>
    <table>
      <tr><td>HS编码</td><td>品名</td><td>实例汇总</td><td>申报要素·退税</td><td>编码对比</td></tr>
      <tr>
        <td><b>61083200.00</b></td>
        <td><span class="showdesc">化纤制针织或钩编女睡衣及睡衣裤</span><br/><span>[Knitted women's pyjamas]</span></td>
        <td><a href="//www.i5a6.com/hscode/detail/6108320000#sbsl">534条</a></td>
        <td><a href="//www.i5a6.com/hscode/detail/6108320000">查看详情</a></td>
        <td>--</td>
      </tr>
    </table>
    "#;
    let bundle = search(html, OBSERVED).unwrap();
    let standard = bundle
        .records
        .iter()
        .find(|record| record.kind == RemoteRecordKind::StandardCode)
        .unwrap();
    assert_eq!(standard.item.code, "6108320000");
    assert_eq!(standard.item.name, "化纤制针织或钩编女睡衣及睡衣裤");
    assert_eq!(standard.item.description, "Knitted women's pyjamas");
    assert_eq!(standard.instance_count, Some(534));
    assert_eq!(
        standard.summary_url,
        "https://www.i5a6.com/hscode/detail/6108320000#sbsl"
    );
}

#[test]
fn declaration_table_stays_reference_only_and_uses_description() {
    let html = r#"
    <div id="hssbsl">申报实例查询结果</div>
    <div id="hscasefind"><table>
      <tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>
      <tr><td><a href="//www.i5a6.com/hscode/detail/6109100010">61091000.10</a></td><td>棉制男T恤</td><td>针织|男式|100%棉</td></tr>
    </table></div>
    "#;
    let bundle = search(html, OBSERVED).unwrap();
    let example = bundle
        .records
        .iter()
        .find(|record| record.kind == RemoteRecordKind::DeclarationExample)
        .unwrap();
    assert_eq!(example.item.code, "6109100010");
    assert_eq!(example.item.description, "针织|男式|100%棉");
    assert_eq!(example.item.elements, "");
    assert_eq!(example.item.status, "ReferenceOnly");
}

#[test]
fn detail_reads_reference_entries_and_examples() {
    let examples = (1..=20)
        .map(|index| {
            format!(
                "<tr><td>61083200.00</td><td>女式睡衣{index}</td><td>针织|女式|100%涤纶</td></tr>"
            )
        })
        .collect::<String>();
    let html = format!(
        r#"
        <div id="hscode-detail"><table>
          <tr><td>商品编码</td><td>61083200.00</td></tr>
          <tr><td>商品名称</td><td>化纤制针织或钩编女睡衣及睡衣裤</td></tr>
          <tr><td>申报要素</td><td>织造方法;类别;成分含量</td></tr>
          <tr><td>法定第一单位</td><td>件</td></tr>
        </table></div>
        <div class="detail-hd"><span>个人行邮税号 「04019900」</span></div>
        <div class="detail-hd">10位HS编码+3位CIQ代码(中国海关申报13位海关编码)</div>
        <table><tr><td class="tdtoth">10位HS编码+3位CIQ代码</td><td class="tdtoth">商品信息</td></tr>
          <tr><td>6108320000.101</td><td>儿童服装</td></tr></table>
        <div class="detail-hd">所属分类及章节、品目</div>
        <table><tr><td>类目</td><td>第十一类 纺织原料及纺织制品</td></tr></table>
        <div class="detail-hd" id="sbsl">申报实例汇总</div>
        <table><tr><td>HS编码</td><td>商品名称</td><td>商品规格</td></tr>{examples}</table>
        "#
    );
    let seed = ApiHsCodeDto {
        code: "6108320000".into(),
        normalized_code: "6108320000".into(),
        detail_url: "https://www.i5a6.com/hscode/detail/6108320000".into(),
        ..Default::default()
    };
    let bundle = detail(&html, &seed, OBSERVED).unwrap();
    assert_eq!(bundle.personal_postal_tax_code, "04019900");
    assert_eq!(bundle.ciq_entries.len(), 1);
    assert_eq!(bundle.classification_entries.len(), 1);
    assert_eq!(bundle.declaration_examples.len(), 20);
    assert_eq!(bundle.item.elements, "织造方法;类别;成分含量");
}
