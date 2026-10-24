import io

p = 'HHCL/Views/ModDetailView.xaml'
s = io.open(p, encoding='utf-8').read()

start = s.index('                                    <!-- 组头：整行可点击，折叠/展开这一组 -->')
end = s.index('                                    </Button>', start) + len('                                    </Button>')

new = '''                                    <!-- 组头：整行可点击，折叠/展开这一组。
                                         样式改成"左侧竖条 + 版本号 + 计数"，比一条灰底蓝字好看 -->
                                    <Button Click="Header_Click">
                                        <Button.Style>
                                            <Style TargetType="Button">
                                                <Setter Property="Background" Value="Transparent" />
                                                <Setter Property="BorderThickness" Value="0" />
                                                <Setter Property="Padding" Value="8,7" />
                                                <Setter Property="Cursor" Value="Hand" />
                                                <Setter Property="Visibility" Value="Collapsed" />
                                                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                                                <Setter Property="Template">
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType="Button">
                                                            <Border x:Name="Bd" Background="{TemplateBinding Background}"
                                                                    CornerRadius="6" Padding="{TemplateBinding Padding}">
                                                                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" />
                                                            </Border>
                                                            <ControlTemplate.Triggers>
                                                                <Trigger Property="IsMouseOver" Value="True">
                                                                    <Setter TargetName="Bd" Property="Background"
                                                                            Value="{StaticResource CardHoverBrush}" />
                                                                </Trigger>
                                                            </ControlTemplate.Triggers>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding IsHeader}" Value="True">
                                                        <Setter Property="Visibility" Value="Visible" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Button.Style>
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="Auto" />
                                                <ColumnDefinition Width="*" />
                                                <ColumnDefinition Width="Auto" />
                                            </Grid.ColumnDefinitions>
                                            <!-- 左侧竖条 -->
                                            <Border Grid.Column="0" Width="3" Height="16" CornerRadius="2"
                                                    Background="{StaticResource AccentBrush}" Margin="0,0,9,0"
                                                    VerticalAlignment="Center" />
                                            <!-- 折叠箭头 -->
                                            <TextBlock Grid.Column="1" FontSize="10" Width="12"
                                                       VerticalAlignment="Center" Margin="0,0,6,0"
                                                       Foreground="{StaticResource TextTertiaryBrush}">
                                                <TextBlock.Style>
                                                    <Style TargetType="TextBlock">
                                                        <Setter Property="Text" Value="&#x25BE;" />
                                                        <Style.Triggers>
                                                            <!-- 折叠时换成向右的箭头 -->
                                                            <DataTrigger Binding="{Binding IsCollapsed}" Value="True">
                                                                <Setter Property="Text" Value="&#x25B8;" />
                                                            </DataTrigger>
                                                        </Style.Triggers>
                                                    </Style>
                                                </TextBlock.Style>
                                            </TextBlock>
                                            <!-- 版本号 -->
                                            <TextBlock Grid.Column="2" Text="{Binding Name}" FontSize="13.5"
                                                       FontWeight="SemiBold" VerticalAlignment="Center"
                                                       Foreground="{StaticResource TextPrimaryBrush}" />
                                            <!-- 文件数 -->
                                            <TextBlock Grid.Column="3" Text="{Binding Size}" FontSize="11.5"
                                                       VerticalAlignment="Center" Margin="12,0,0,0"
                                                       Foreground="{StaticResource TextTertiaryBrush}" />
                                        </Grid>
                                    </Button>'''

s = s[:start] + new + s[end:]
io.open(p, 'w', encoding='utf-8').write(s)
print('组头样式已重做')
